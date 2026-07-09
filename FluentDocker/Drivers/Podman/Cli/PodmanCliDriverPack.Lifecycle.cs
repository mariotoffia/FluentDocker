using System;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Podman.Cli
{
  public partial class PodmanCliDriverPack
  {
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      await _initializeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
        _drivers.Clear();
        _initialized = false;
        _context = null;
        _binaryResolver = null;
      }
      finally
      {
        _initializeLock.Release();
        _initializeLock.Dispose();
      }
      GC.SuppressFinalize(this);
    }

    private void ThrowIfNotInitialized()
    {
      ThrowIfDisposed();
      if (!_initialized)
        throw new InvalidOperationException(
            "PodmanCliDriverPack has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
  }
}
