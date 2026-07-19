using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;

namespace FluentDocker.Drivers.Docker.Cli
{
  public partial class DockerCliDriverPack
  {
    /// <summary>
    /// Disposes pack-owned resources — currently the inference connection's
    /// <see cref="System.Net.Http.HttpClient"/>. Invoked by the kernel's driver
    /// registry when the kernel is disposed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      ModelApiConnection? connection = null;
      await _initializeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
        lock (_inferenceLock)
        {
          connection = _modelInferenceConnection;
          _modelInferenceConnection = null;
          _modelInferenceDriver = null;
        }

        // Do not Clear() _drivers: resolution reads it lock-free (IDriverPack contract),
        // so mutating it here is a torn-read data race with an in-flight resolver. The
        // _disposed guard fences new callers; the dictionary stays immutable after init.
        Volatile.Write(ref _initialized, false);
        _context = null!;
        _binaryResolver = null!;
      }
      finally
      {
        // Do not dispose _initializeLock: a concurrent InitializeAsync may be queued in
        // WaitAsync; disposing it would hang/mask instead of throwing ObjectDisposedException.
        // SemaphoreSlim owns no unmanaged resource here (AvailableWaitHandle unused).
        _initializeLock.Release();
      }

      if (connection != null)
        await connection.DisposeAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private void ThrowIfNotInitialized()
    {
      ThrowIfDisposed();
      if (!Volatile.Read(ref _initialized))
        throw new InvalidOperationException(
            "DockerCliDriverPack has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
  }
}
