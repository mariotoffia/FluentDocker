#nullable disable warnings
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
        // Do not Clear() _drivers: resolution reads it lock-free (IDriverPack contract),
        // so mutating it here is a torn-read data race with an in-flight resolver. The
        // _disposed guard fences new callers; the dictionary stays immutable after init.
        Volatile.Write(ref _initialized, false);
        _context = null;
        _binaryResolver = null;
      }
      finally
      {
        // Do not dispose _initializeLock: a concurrent InitializeAsync may be queued in
        // WaitAsync; disposing it would hang/mask instead of throwing ObjectDisposedException.
        _initializeLock.Release();
      }
      GC.SuppressFinalize(this);
    }

    private void ThrowIfNotInitialized()
    {
      ThrowIfDisposed();
      if (!Volatile.Read(ref _initialized))
        throw new InvalidOperationException(
            "PodmanCliDriverPack has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
  }
}
