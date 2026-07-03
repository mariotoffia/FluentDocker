using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Kernel;
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
      if (Volatile.Read(ref _disposed) == 2)
        return;

      if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 2)
        return;

      using var disposeCts = new CancellationTokenSource(DisposeBudget);
      var timeoutMs = DisposeBudget.TotalMilliseconds;
      var lockTaken = false;
      try
      {
        await _registrationLock.WaitAsync(disposeCts.Token).ConfigureAwait(false);
        lockTaken = true;

        if (Volatile.Read(ref _disposed) == 2)
          return;

        foreach (var kvp in _driverPacks)
          await DisposeDriverPackWithinBudgetAsync(
              kvp.Value.DriverPack, _logger, kvp.Key, timeoutMs, disposeCts.Token).ConfigureAwait(false);

        foreach (var kvp in _drivers)
          await DisposeDriverWithinBudgetAsync(
              kvp.Value.Driver, _logger, kvp.Key, timeoutMs, disposeCts.Token).ConfigureAwait(false);

        _driverPacks.Clear();
        _drivers.Clear();
        Interlocked.Exchange(ref _disposed, 2);
      }
      catch (OperationCanceledException) when (disposeCts.IsCancellationRequested && !lockTaken)
      {
        _logger.LogWarning(
            "Driver registry disposal timed out waiting for registration lock after {TimeoutMs} ms",
            timeoutMs);
      }
      finally
      {
        if (lockTaken)
          _registrationLock.Release();
        // Do not dispose SemaphoreSlim; waiters may still be queued and it owns no unmanaged resource.
        if (Volatile.Read(ref _disposed) == 2)
          GC.SuppressFinalize(this);
      }
    }

    #endregion

    /// <summary>
    /// Total wall-clock budget for waiting on registration and disposal work.
    /// </summary>
    protected virtual TimeSpan DisposeBudget =>
        TimeSpan.FromMilliseconds(BuildResults.DefaultDisposeBudgetMs);

    private static async Task DisposeDriverWithinBudgetAsync(
        IDriver driver, ILogger logger, string driverId, double timeoutMs, CancellationToken cancellationToken)
    {
      try
      {
        await DisposeDriverSafelyAsync(driver, logger, driverId)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        logger.LogWarning(
            "Timed out disposing driver {DriverId} after {TimeoutMs} ms",
            driverId, timeoutMs);
      }
    }

    private static async Task DisposeDriverPackWithinBudgetAsync(
        IDriverPack driverPack, ILogger logger, string driverId, double timeoutMs, CancellationToken cancellationToken)
    {
      try
      {
        await DisposeDriverPackSafelyAsync(driverPack, logger, driverId)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        logger.LogWarning(
            "Timed out disposing driver pack {DriverId} after {TimeoutMs} ms",
            driverId, timeoutMs);
      }
    }

    private static async Task DisposeDriverSafelyAsync(
        IDriver driver, ILogger logger, string driverId = null)
    {
      try
      {
        if (driver is IAsyncDisposable asyncDisposable)
          await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (driver is IDisposable disposable)
          await Task.Run(disposable.Dispose).ConfigureAwait(false);
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
          await Task.Run(disposable.Dispose).ConfigureAwait(false);
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
