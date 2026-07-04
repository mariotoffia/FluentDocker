using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Models.Connection
{
  public sealed partial class ModelApiConnection
  {
    private HttpResponseMessage ApplyBodyTimeout(HttpResponseMessage response)
    {
      if (_requestTimeout != Timeout.InfiniteTimeSpan && response.Content is not null)
        response.Content = new TimeoutHttpContent(response.Content, _requestTimeout);
      return response;
    }

    private sealed class TimeoutHttpContent : HttpContent
    {
      private readonly HttpContent _inner;
      private readonly TimeSpan _timeout;

      public TimeoutHttpContent(HttpContent inner, TimeSpan timeout)
      {
        _inner = inner;
        _timeout = timeout;
        foreach (var header in inner.Headers)
          Headers.TryAddWithoutValidation(header.Key, header.Value);
      }

      protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
          SerializeToStreamAsync(stream, context, CancellationToken.None);

      protected override async Task SerializeToStreamAsync(
          Stream stream, TransportContext context, CancellationToken cancellationToken)
      {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);
        try
        {
          await _inner.CopyToAsync(stream, context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw new TimeoutException(
              $"The model API response body exceeded the configured request timeout of {_timeout}.", ex);
        }
      }

      protected override async Task<Stream> CreateContentReadStreamAsync()
      {
        var stream = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
        return new TimeoutReadStream(stream, _timeout);
      }

      protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
      {
        var stream = await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new TimeoutReadStream(stream, _timeout);
      }

      protected override bool TryComputeLength(out long length)
      {
        length = _inner.Headers.ContentLength ?? -1;
        return length >= 0;
      }

      protected override void Dispose(bool disposing)
      {
        if (disposing)
          _inner.Dispose();
        base.Dispose(disposing);
      }
    }

    private sealed class TimeoutReadStream(Stream inner, TimeSpan timeout) : Stream
    {
      public override bool CanRead => inner.CanRead;
      public override bool CanSeek => inner.CanSeek;
      public override bool CanWrite => inner.CanWrite;
      public override long Length => inner.Length;
      public override long Position { get => inner.Position; set => inner.Position = value; }

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
          return await inner.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw new TimeoutException($"The model API response body exceeded the configured request timeout of {timeout}.", ex);
        }
      }

      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
          return await inner.ReadAsync(buffer.AsMemory(offset, count), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw new TimeoutException($"The model API response body exceeded the configured request timeout of {timeout}.", ex);
        }
      }

      public override int Read(byte[] buffer, int offset, int count) =>
          ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
      public override void Flush() => inner.Flush();
      public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
      public override void SetLength(long value) => inner.SetLength(value);
      public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
      protected override void Dispose(bool disposing)
      {
        if (disposing)
          inner.Dispose();
        base.Dispose(disposing);
      }
    }
  }
}
