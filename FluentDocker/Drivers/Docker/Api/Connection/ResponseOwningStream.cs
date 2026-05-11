using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// A stream wrapper that takes ownership of an <see cref="HttpResponseMessage"/>
  /// and disposes it when the stream is disposed. This prevents resource leaks
  /// when streaming responses from the Docker API.
  /// </summary>
  public sealed class ResponseOwningStream(Stream inner, HttpResponseMessage response) : Stream
  {
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly HttpResponseMessage _response = response ?? throw new ArgumentNullException(nameof(response));
    private int _disposed;

    public override bool CanRead => !IsDisposed && _inner.CanRead;
    public override bool CanSeek => !IsDisposed && _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
      get => _inner.Position;
      set => _inner.Position = value;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      ThrowIfDisposed();
      return _inner.Read(buffer, offset, count);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      ThrowIfDisposed();
      return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void Flush() => _inner.Flush();

    protected override void Dispose(bool disposing)
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      if (disposing)
      {
        _inner.Dispose();
        _response.Dispose();
      }

      base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      await _inner.DisposeAsync().ConfigureAwait(false);
      _response.Dispose();

      await base.DisposeAsync().ConfigureAwait(false);
    }
  }
}
