#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  // Wraps request content so a callback fires after every chunk written to the transport, letting a
  // watchdog detect an upload that has stopped making progress (daemon wedged mid-upload).
  internal sealed class ProgressObservingHttpContent : HttpContent
  {
    private readonly HttpContent _inner;
    private readonly Action _onCompleted;
    private readonly Action _onProgress;

    public ProgressObservingHttpContent(HttpContent inner, Action onProgress, Action onCompleted)
    {
      _inner = inner;
      _onCompleted = onCompleted;
      _onProgress = onProgress;
      foreach (var header in inner.Headers)
        Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
      await using var progress = new ProgressWriteStream(stream, _onProgress);
      await _inner.CopyToAsync(progress, cancellationToken).ConfigureAwait(false);
      _onCompleted();
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override bool TryComputeLength(out long length)
    {
      var contentLength = _inner.Headers.ContentLength;
      if (contentLength.HasValue)
      {
        length = contentLength.Value;
        return true;
      }
      length = 0;
      return false;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        _inner.Dispose();
      base.Dispose(disposing);
    }

    private sealed class ProgressWriteStream(Stream inner, Action onProgress) : Stream
    {
      public override bool CanRead => false;
      public override bool CanSeek => false;
      public override bool CanWrite => true;
      public override long Length => throw new NotSupportedException();
      public override long Position
      {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }

      public override async ValueTask WriteAsync(
          ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
      {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        onProgress();
      }

      public override Task WriteAsync(
          byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
          WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

      public override void Write(byte[] buffer, int offset, int count)
      {
        inner.Write(buffer, offset, count);
        onProgress();
      }

      public override async Task FlushAsync(CancellationToken cancellationToken) =>
          await inner.FlushAsync(cancellationToken).ConfigureAwait(false);

      public override void Flush() => inner.Flush();

      public override int Read(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException();
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
    }
  }
}
