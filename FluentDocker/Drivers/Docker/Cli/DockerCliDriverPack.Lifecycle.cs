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

      ModelApiConnection connection = null;
      await _initializeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
        lock (_inferenceLock)
        {
          connection = _modelInferenceConnection;
          _modelInferenceConnection = null;
          _modelInferenceDriver = null;
        }

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

      if (connection != null)
        await connection.DisposeAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private void ThrowIfNotInitialized()
    {
      ThrowIfDisposed();
      if (!_initialized)
        throw new InvalidOperationException(
            "DockerCliDriverPack has not been initialized. Call InitializeAsync first.");
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
  }
}
