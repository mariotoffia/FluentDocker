using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// A read-only <see cref="Stream"/> that owns both the underlying response body
  /// stream and its <see cref="HttpResponseMessage"/>, so disposing the stream
  /// disposes the response (preventing connection/handler leaks during streaming).
  /// </summary>
  public sealed class ResponseOwningStream(Stream inner, HttpResponseMessage response) : Stream
  {
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly HttpResponseMessage _response = response ?? throw new ArgumentNullException(nameof(response));
    private int _disposed;

    /// <inheritdoc />
    public override bool CanRead => !IsDisposed && _inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => !IsDisposed && _inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position { get => _inner.Position; set => _inner.Position = value; }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
      ThrowIfDisposed();
      return _inner.Read(buffer, offset, count);
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
      ThrowIfDisposed();
      return _inner.Seek(offset, origin);
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      if (disposing)
      {
        // Dispose the response in a finally so it ALWAYS runs even when the inner
        // stream's dispose throws — otherwise the HttpResponseMessage (and its pooled
        // connection) would leak. Any exception from the inner dispose still propagates
        // after the response has been disposed.
        try
        {
          _inner.Dispose();
        }
        finally
        {
          _response.Dispose();
        }
      }

      base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      // Dispose the response in a finally so it ALWAYS runs even when the inner stream's
      // DisposeAsync throws — otherwise the HttpResponseMessage (and its pooled connection)
      // would leak. Any exception from the inner dispose still propagates after the response
      // has been disposed.
      try
      {
        await _inner.DisposeAsync().ConfigureAwait(false);
      }
      finally
      {
        _response.Dispose();
      }

      await base.DisposeAsync().ConfigureAwait(false);
    }
  }
}
