using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Default implementation of the driver registry.
  /// Supports both individual drivers and driver packs.
  /// </summary>
  public class DriverRegistry : IDriverRegistry, IDisposable, IAsyncDisposable
  {
    private readonly ConcurrentDictionary<string, DriverRegistration> _drivers = new ConcurrentDictionary<string, DriverRegistration>();
    private readonly ConcurrentDictionary<string, DriverPackRegistration> _driverPacks = new ConcurrentDictionary<string, DriverPackRegistration>();
    private readonly SemaphoreSlim _registrationLock = new SemaphoreSlim(1, 1);
    private string _defaultDriverId;
    private readonly object _defaultDriverLock = new object();

    #region Driver Registration

    /// <summary>
    /// Registers a driver with initialization.
    /// Uses a lock to prevent TOCTOU races between the existence check,
    /// initialization, and registration.
    /// </summary>
    public async Task RegisterAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(driverId))
        throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));

      ArgumentNullException.ThrowIfNull(driver);
      ArgumentNullException.ThrowIfNull(context);

      await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        // Check if ID is already used by a driver pack or driver
        if (_driverPacks.ContainsKey(driverId))
          throw new DriverException($"Driver ID '{driverId}' is already registered as a driver pack", ErrorCodes.Driver.AlreadyRegistered);

        if (_drivers.ContainsKey(driverId))
          throw new DriverException($"Driver '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);

        // Ensure context has the driver ID
        if (string.IsNullOrEmpty(context.DriverId))
          context.DriverId = driverId;

        // Initialize the driver
        await driver.InitializeAsync(context, cancellationToken).ConfigureAwait(false);

        var registration = new DriverRegistration
        {
          Driver = driver,
          Context = context,
          Type = driver.Type,
          Runtime = driver.Runtime
        };

        // Add should always succeed since we hold the lock and checked above.
        // If it somehow fails, dispose the initialized driver.
        if (!_drivers.TryAdd(driverId, registration))
        {
          await DisposeDriverSafelyAsync(driver).ConfigureAwait(false);
          throw new DriverException($"Driver '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);
        }

        // Set as default if this is the first driver
        SetDefaultIfFirst(driverId);
      }
      finally
      {
        _registrationLock.Release();
      }
    }

    /// <summary>
    /// Unregisters a driver or driver pack.
    /// </summary>
    public void Unregister(string driverId)
    {
      _drivers.TryRemove(driverId, out _);
      _driverPacks.TryRemove(driverId, out _);

      lock (_defaultDriverLock)
      {
        if (_defaultDriverId == driverId)
        {
          _defaultDriverId = null;
        }
      }
    }

    /// <summary>
    /// Gets a driver by ID.
    /// </summary>
    public IDriver GetDriver(string driverId)
    {
      if (!_drivers.TryGetValue(driverId, out var registration))
      {
        throw new DriverNotFoundException(driverId);
      }

      return registration.Driver;
    }

    /// <summary>
    /// Tries to get a driver by ID.
    /// </summary>
    public bool TryGetDriver(string driverId, out IDriver driver)
    {
      if (_drivers.TryGetValue(driverId, out var registration))
      {
        driver = registration.Driver;
        return true;
      }

      driver = null;
      return false;
    }

    #endregion

    #region Driver Pack Registration

    /// <summary>
    /// Registers a driver pack with initialization.
    /// Uses a lock to prevent TOCTOU races between the existence check,
    /// initialization, and registration.
    /// </summary>
    public async Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(driverId))
        throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));

      ArgumentNullException.ThrowIfNull(driverPack);
      ArgumentNullException.ThrowIfNull(context);

      await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        // Check if ID is already used by a regular driver or driver pack
        if (_drivers.ContainsKey(driverId))
          throw new DriverException($"Driver ID '{driverId}' is already registered as a driver", ErrorCodes.Driver.AlreadyRegistered);

        if (_driverPacks.ContainsKey(driverId))
          throw new DriverException($"Driver pack '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);

        // Ensure context has the driver ID
        if (string.IsNullOrEmpty(context.DriverId))
          context.DriverId = driverId;

        // Initialize the driver pack
        await driverPack.InitializeAsync(context, cancellationToken).ConfigureAwait(false);

        var registration = new DriverPackRegistration
        {
          DriverPack = driverPack,
          Context = context,
          Type = driverPack.Type,
          Runtime = driverPack.Runtime
        };

        // Add should always succeed since we hold the lock and checked above.
        // If it somehow fails, dispose the initialized driver pack.
        if (!_driverPacks.TryAdd(driverId, registration))
        {
          await DisposeDriverPackSafelyAsync(driverPack).ConfigureAwait(false);
          throw new DriverException($"Driver pack '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);
        }

        // Set as default if this is the first driver/pack
        SetDefaultIfFirst(driverId);
      }
      finally
      {
        _registrationLock.Release();
      }
    }

    /// <summary>
    /// Gets a driver pack by ID.
    /// </summary>
    public IDriverPack GetDriverPack(string driverId)
    {
      if (!_driverPacks.TryGetValue(driverId, out var registration))
      {
        throw new DriverNotFoundException(driverId);
      }

      return registration.DriverPack;
    }

    /// <summary>
    /// Tries to get a driver pack by ID.
    /// </summary>
    public bool TryGetDriverPack(string driverId, out IDriverPack driverPack)
    {
      if (_driverPacks.TryGetValue(driverId, out var registration))
      {
        driverPack = registration.DriverPack;
        return true;
      }

      driverPack = null;
      return false;
    }

    /// <summary>
    /// Checks if a driver ID refers to a driver pack.
    /// </summary>
    public bool IsDriverPack(string driverId)
    {
      return _driverPacks.ContainsKey(driverId);
    }

    #endregion

    #region Context and Status

    /// <summary>
    /// Gets the driver context for a driver or driver pack.
    /// </summary>
    public DriverContext GetContext(string driverId)
    {
      if (_drivers.TryGetValue(driverId, out var driverReg))
      {
        return driverReg.Context;
      }

      if (_driverPacks.TryGetValue(driverId, out var packReg))
      {
        return packReg.Context;
      }

      throw new DriverNotFoundException(driverId);
    }

    /// <summary>
    /// Checks if a driver or driver pack is registered.
    /// </summary>
    public bool IsRegistered(string driverId)
    {
      return _drivers.ContainsKey(driverId) || _driverPacks.ContainsKey(driverId);
    }

    /// <summary>
    /// Gets all registered driver IDs (both drivers and driver packs).
    /// </summary>
    public IReadOnlyList<string> GetAllDriverIds()
    {
      return _drivers.Keys.Concat(_driverPacks.Keys).ToList();
    }

    #endregion

    #region Filtering

    /// <summary>
    /// Gets drivers and driver packs by type.
    /// </summary>
    public IReadOnlyList<string> GetDriversByType(DriverType driverType)
    {
      var fromDrivers = _drivers
          .Where(kvp => kvp.Value.Type == driverType)
          .Select(kvp => kvp.Key);

      var fromPacks = _driverPacks
          .Where(kvp => kvp.Value.Type == driverType)
          .Select(kvp => kvp.Key);

      return fromDrivers.Concat(fromPacks).ToList();
    }

    /// <summary>
    /// Gets drivers and driver packs by runtime.
    /// </summary>
    public IReadOnlyList<string> GetDriversByRuntime(RuntimeType runtime)
    {
      var fromDrivers = _drivers
          .Where(kvp => kvp.Value.Runtime == runtime)
          .Select(kvp => kvp.Key);

      var fromPacks = _driverPacks
          .Where(kvp => kvp.Value.Runtime == runtime)
          .Select(kvp => kvp.Key);

      return fromDrivers.Concat(fromPacks).ToList();
    }

    #endregion

    #region Default Driver

    /// <summary>
    /// Gets the default driver ID.
    /// </summary>
    public string GetDefaultDriverId()
    {
      lock (_defaultDriverLock)
      {
        return _defaultDriverId;
      }
    }

    /// <summary>
    /// Sets the default driver ID.
    /// </summary>
    public void SetDefaultDriver(string driverId)
    {
      lock (_defaultDriverLock)
      {
        // Check inside lock to prevent TOCTOU race where another thread
        // could unregister the driver between check and set.
        if (!IsRegistered(driverId))
          throw new DriverNotFoundException(driverId);

        _defaultDriverId = driverId;
      }
    }

    #endregion

    #region Private Helpers

    private void SetDefaultIfFirst(string driverId)
    {
      lock (_defaultDriverLock)
      {
        if (_defaultDriverId == null)
        {
          _defaultDriverId = driverId;
        }
      }
    }

    /// <summary>
    /// Safely disposes a driver that was initialized but could not be registered.
    /// </summary>
    private static async Task DisposeDriverSafelyAsync(IDriver driver)
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
        Logger.Log($"Driver disposal cleanup failed: {ex.Message}");
      }
    }

    /// <summary>
    /// Safely disposes a driver pack that was initialized but could not be registered.
    /// </summary>
    private static async Task DisposeDriverPackSafelyAsync(IDriverPack driverPack)
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
        Logger.Log($"Driver pack disposal cleanup failed: {ex.Message}");
      }
    }

    #endregion

    #region IDisposable / IAsyncDisposable

    public void Dispose()
    {
      DisposeAsync().AsTask().GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      // Dispose all registered driver packs
      foreach (var kvp in _driverPacks)
      {
        try
        {
          if (kvp.Value.DriverPack is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
          else if (kvp.Value.DriverPack is IDisposable disposable)
            disposable.Dispose();
        }
        catch (Exception ex)
        {
          Logger.Log($"Failed to dispose driver pack '{kvp.Key}': {ex.Message}");
        }
      }

      // Dispose all registered drivers
      foreach (var kvp in _drivers)
      {
        try
        {
          if (kvp.Value.Driver is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
          else if (kvp.Value.Driver is IDisposable disposable)
            disposable.Dispose();
        }
        catch (Exception ex)
        {
          Logger.Log($"Failed to dispose driver '{kvp.Key}': {ex.Message}");
        }
      }

      _driverPacks.Clear();
      _drivers.Clear();
      _registrationLock.Dispose();

      GC.SuppressFinalize(this);
    }

    #endregion

    #region Registration Types

    private sealed class DriverRegistration
    {
      public IDriver Driver { get; set; }
      public DriverContext Context { get; set; }
      public DriverType Type { get; set; }
      public RuntimeType Runtime { get; set; }
    }

    private sealed class DriverPackRegistration
    {
      public IDriverPack DriverPack { get; set; }
      public DriverContext Context { get; set; }
      public DriverType Type { get; set; }
      public RuntimeType Runtime { get; set; }
    }

    #endregion
  }
}
