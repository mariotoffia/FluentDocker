using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  public partial class DriverRegistry
  {
    #region IDisposable / IAsyncDisposable

#pragma warning disable CA1816
    /// <summary>
    /// Synchronously disposes the registry and its registered drivers/packs.
    /// Disposal order follows the registry dictionaries' enumeration order and is not ordered.
    /// </summary>
    public void Dispose()
    {
      Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }
#pragma warning restore CA1816

    /// <summary>
    /// Asynchronously disposes the registry and its registered drivers/packs.
    /// Disposal order follows the registry dictionaries' enumeration order and is not ordered.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      await _registrationLock.WaitAsync().ConfigureAwait(false);
      try
      {
        foreach (var kvp in _driverPacks)
          await DisposeDriverPackSafelyAsync(kvp.Value.DriverPack, _logger, kvp.Key).ConfigureAwait(false);

        foreach (var kvp in _drivers)
          await DisposeDriverSafelyAsync(kvp.Value.Driver, _logger, kvp.Key).ConfigureAwait(false);

        _driverPacks.Clear();
        _drivers.Clear();
      }
      finally
      {
        _registrationLock.Release();
        // Do not dispose SemaphoreSlim; waiters may still be queued and it owns no unmanaged resource.
        GC.SuppressFinalize(this);
      }
    }

    #endregion

    private static void DisposeDriverSynchronously(IDriver driver, ILogger logger)
    {
      Task.Run(() => DisposeDriverSafelyAsync(driver, logger)).GetAwaiter().GetResult();
    }

    private static void DisposeDriverPackSynchronously(IDriverPack driverPack, ILogger logger)
    {
      Task.Run(() => DisposeDriverPackSafelyAsync(driverPack, logger)).GetAwaiter().GetResult();
    }

    private static async Task DisposeDriverSafelyAsync(
        IDriver driver, ILogger logger, string driverId = null)
    {
      try
      {
        if (driver is IAsyncDisposable asyncDisposable)
          await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (driver is IDisposable disposable)
          disposable.Dispose();
      }
      catch (Exception ex)
      {
        if (driverId == null)
          logger.LogWarning(ex, "Driver disposal cleanup failed");
        else
          logger.LogWarning(ex, "Failed to dispose driver {DriverId}", driverId);
      }
    }

    private static async Task DisposeDriverPackSafelyAsync(
        IDriverPack driverPack, ILogger logger, string driverId = null)
    {
      try
      {
        if (driverPack is IAsyncDisposable asyncDisposable)
          await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (driverPack is IDisposable disposable)
          disposable.Dispose();
      }
      catch (Exception ex)
      {
        if (driverId == null)
          logger.LogWarning(ex, "Driver pack disposal cleanup failed");
        else
          logger.LogWarning(ex, "Failed to dispose driver pack {DriverId}", driverId);
      }
    }
  }
}
