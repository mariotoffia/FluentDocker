using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
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
    /// A registration-lock timeout is retried once (a later <see cref="DisposeAsync"/> retry
    /// still proceeds — see its remarks); if the retry also times out, the remaining live
    /// drivers are counted in <see cref="AbandonedDriverCount"/> and an error is logged so
    /// the leak is observable rather than silent.
    /// </summary>
    public void Dispose()
    {
      try
      {
        Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
      }
      catch (TimeoutException)
      {
        // One retry: the lock holder (a concurrent registration or disposer) usually
        // finishes within a second budget window — mirrors the kernel-level retry idiom.
        try
        {
          Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
        catch (TimeoutException ex)
        {
          // Surface the incomplete state loudly: every remaining driver is left alive while
          // registry methods throw ObjectDisposedException. Account them as abandoned so
          // AbandonedDriverCount reflects reality for leak monitors.
          var leaked = _driverPacks.Count + _drivers.Count;
          if (leaked > 0)
            Interlocked.Add(ref _abandonedDriverCount, leaked);
          _logger.LogError(ex,
              "Driver registry sync disposal timed out twice; {LeakedCount} driver(s)/pack(s) left undisposed",
              leaked);
        }
      }
    }
#pragma warning restore CA1816

    /// <summary>
    /// Asynchronously disposes the registry and its registered drivers/packs.
    /// Disposal order follows the registry dictionaries' enumeration order and is not ordered.
    /// Throws <see cref="TimeoutException"/> if the registration lock cannot be acquired within
    /// <see cref="DisposeBudget"/>; the instance stays disposable and a later call retries.
    /// If the lock is acquired but an individual driver or pack exhausts the remaining disposal
    /// budget, that instance is abandoned, <see cref="DriverRegistry.AbandonedDriverCount"/> is
    /// incremented, and disposal can still complete so callers can detect partial cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
      if (Volatile.Read(ref _disposed) == 2)
        return;

      if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 2)
        return;

      var deadline = DateTimeOffset.UtcNow + DisposeBudget;
      var timeoutMs = DisposeBudget.TotalMilliseconds;
      var lockTaken = false;
      try
      {
        if (!await _registrationLock.WaitAsync(Remaining(deadline)).ConfigureAwait(false))
          throw new TimeoutException("Timed out waiting for driver registration lock during disposal.");
        lockTaken = true;

        if (Volatile.Read(ref _disposed) == 2)
          return;

        foreach (var kvp in _driverPacks)
        {
          if (!await DisposeDriverPackWithinBudgetAsync(
              kvp.Value.DriverPack, _logger, kvp.Key, Remaining(deadline)).ConfigureAwait(false))
            Interlocked.Increment(ref _abandonedDriverCount);
        }

        foreach (var kvp in _drivers)
        {
          if (!await DisposeDriverWithinBudgetAsync(
              kvp.Value.Driver, _logger, kvp.Key, Remaining(deadline)).ConfigureAwait(false))
            Interlocked.Increment(ref _abandonedDriverCount);
        }

        _driverPacks.Clear();
        _drivers.Clear();
        Interlocked.Exchange(ref _disposed, 2);
      }
      catch (TimeoutException) when (!lockTaken)
      {
        // Do NOT reset _disposed back to 0 here. A concurrent disposer may already hold the lock
        // and be tearing drivers down; resurrecting the registry to "alive" would let off-lock
        // readers observe half-disposed drivers. Leaving _disposed == 1 keeps readers correctly
        // throwing ObjectDisposedException, and a later retry still proceeds because the fast-path
        // gate only short-circuits once disposal has fully completed (_disposed == 2).
        _logger.LogWarning(
            "Driver registry disposal timed out waiting for registration lock after {TimeoutMs} ms",
            timeoutMs);
        throw;
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
    /// Total wall-clock budget for acquiring the registration lock and disposing all registered
    /// drivers/packs. Each item receives only the remaining budget.
    /// </summary>
    protected virtual TimeSpan DisposeBudget =>
        TimeSpan.FromMilliseconds(DefaultDisposeBudgetMs);

    /// <summary>
    /// Default kernel/registry dispose budget in milliseconds.
    /// </summary>
    public const int DefaultDisposeBudgetMs = 60_000;

    private static async Task<bool> DisposeDriverWithinBudgetAsync(
        IDriver driver,
        ILogger logger,
        string driverId,
        TimeSpan disposeBudget)
    {
      if (disposeBudget <= TimeSpan.Zero)
      {
        logger.LogWarning("Timed out before disposing driver {DriverId}", driverId);
        return false;
      }

      using var cts = new CancellationTokenSource(disposeBudget);
      try
      {
        await DisposeDriverSafelyAsync(driver, logger, driverId)
            .WaitAsync(cts.Token).ConfigureAwait(false);
        return true;
      }
      catch (OperationCanceledException) when (cts.IsCancellationRequested)
      {
        logger.LogWarning(
            "Timed out disposing driver {DriverId} after {TimeoutMs} ms",
            driverId, disposeBudget.TotalMilliseconds);
        return false;
      }
    }

    private static async Task<bool> DisposeDriverPackWithinBudgetAsync(
        IDriverPack driverPack,
        ILogger logger,
        string driverId,
        TimeSpan disposeBudget)
    {
      if (disposeBudget <= TimeSpan.Zero)
      {
        logger.LogWarning("Timed out before disposing driver pack {DriverId}", driverId);
        return false;
      }

      using var cts = new CancellationTokenSource(disposeBudget);
      try
      {
        await DisposeDriverPackSafelyAsync(driverPack, logger, driverId)
            .WaitAsync(cts.Token).ConfigureAwait(false);
        return true;
      }
      catch (OperationCanceledException) when (cts.IsCancellationRequested)
      {
        logger.LogWarning(
            "Timed out disposing driver pack {DriverId} after {TimeoutMs} ms",
            driverId, disposeBudget.TotalMilliseconds);
        return false;
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

    private static TimeSpan Remaining(DateTimeOffset deadline)
    {
      var remaining = deadline - DateTimeOffset.UtcNow;
      return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
  }
}
