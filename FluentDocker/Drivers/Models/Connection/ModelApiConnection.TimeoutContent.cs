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
    private HttpResponseMessage ApplyBodyTimeout(HttpResponseMessage response, DateTimeOffset deadline)
    {
      if (_requestTimeout != Timeout.InfiniteTimeSpan && response.Content is not null)
        response.Content = new TimeoutHttpContent(response.Content, _requestTimeout, deadline);
      return response;
    }

    private sealed class TimeoutHttpContent : HttpContent
    {
      private readonly HttpContent _inner;
      private readonly TimeSpan _timeout;
      private readonly DateTimeOffset _deadline;

      public TimeoutHttpContent(HttpContent inner, TimeSpan timeout, DateTimeOffset deadline)
      {
        _inner = inner;
        _timeout = timeout;
        _deadline = deadline;
        foreach (var header in inner.Headers)
          Headers.TryAddWithoutValidation(header.Key, header.Value);
      }

      protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
          throw AsyncOnlyNotSupported();

      protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
          SerializeToStreamAsync(stream, context, CancellationToken.None);

      protected override async Task SerializeToStreamAsync(
          Stream stream, TransportContext? context, CancellationToken cancellationToken)
      {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RemainingTimeout());
        try
        {
          await _inner.CopyToAsync(stream, context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw BodyTimeout(ex);
        }
      }

      protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
          throw AsyncOnlyNotSupported();

      protected override async Task<Stream> CreateContentReadStreamAsync()
      {
        var stream = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
        return new TimeoutReadStream(stream, _timeout, _deadline);
      }

      protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
      {
        var stream = await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new TimeoutReadStream(stream, _timeout, _deadline);
      }

      private TimeSpan RemainingTimeout()
      {
        var remaining = _deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
          throw BodyTimeout();
        return remaining < _timeout ? remaining : _timeout;
      }

      private TimeoutException BodyTimeout(Exception? inner = null) =>
          new($"The model API response body exceeded the configured request timeout of {_timeout}.", inner);

      private static NotSupportedException AsyncOnlyNotSupported() =>
          new("Synchronous model API response body reads are not supported; use async HttpContent read APIs.");

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

    private sealed class TimeoutReadStream(Stream inner, TimeSpan timeout, DateTimeOffset deadline) : Stream
    {
      public override bool CanRead => inner.CanRead;
      public override bool CanSeek => inner.CanSeek;
      public override bool CanWrite => false;
      public override long Length => inner.Length;
      public override long Position { get => inner.Position; set => inner.Position = value; }

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RemainingTimeout());
        try
        {
          return await inner.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw BodyTimeout(ex);
        }
      }

      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RemainingTimeout());
        try
        {
          return await inner.ReadAsync(buffer.AsMemory(offset, count), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw BodyTimeout(ex);
        }
      }

      public override int Read(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException("Synchronous model API response body reads are not supported; use ReadAsync.");
      public override void Flush()
      {
      }

      public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException("Model API response body streams are read-only.");
      protected override void Dispose(bool disposing)
      {
        if (disposing)
          inner.Dispose();
        base.Dispose(disposing);
      }

      private TimeSpan RemainingTimeout()
      {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
          throw BodyTimeout();
        return remaining < timeout ? remaining : timeout;
      }

      private TimeoutException BodyTimeout(Exception? innerException = null) =>
          new($"The model API response body exceeded the configured request timeout of {timeout}.", innerException);
    }
  }
}
