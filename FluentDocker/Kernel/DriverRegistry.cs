using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Default implementation of the driver registry.
  /// Supports both individual drivers and driver packs.
  /// </summary>
  public partial class DriverRegistry : IDriverRegistry, IDisposable, IAsyncDisposable
  {
    private readonly ConcurrentDictionary<string, DriverRegistration> _drivers = new();
    private readonly ConcurrentDictionary<string, DriverPackRegistration> _driverPacks = new();
    private readonly HashSet<string> _reservedDriverIds = [];
    private readonly HashSet<object> _reservedDriverInstances = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _registrationOrder = [];
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DriverRegistry> _logger;
    private string _defaultDriverId;
    private readonly object _defaultDriverLock = new object();
    private int _disposed;
    private int _abandonedDriverCount;

    /// <summary>
    /// Creates a new driver registry with the consumer-supplied logger factory.
    /// </summary>
    /// <param name="loggerFactory">Logger factory; required, must not be null.
    /// Pass <see cref="Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance"/> to suppress logging.</param>
    public DriverRegistry(ILoggerFactory loggerFactory)
    {
      ArgumentNullException.ThrowIfNull(loggerFactory);
      _loggerFactory = loggerFactory;
      _logger = loggerFactory.CreateLogger<DriverRegistry>();
    }

    /// <summary>
    /// Logger factory provided at construction. Exposed so packs/services
    /// constructed by the registry can create their own typed loggers.
    /// </summary>
    public ILoggerFactory LoggerFactory => _loggerFactory;

    /// <summary>
    /// Gets how many drivers or driver packs were abandoned because disposal
    /// exhausted the configured budget before their dispose operation completed.
    /// </summary>
    public int AbandonedDriverCount => Volatile.Read(ref _abandonedDriverCount);

    /// <inheritdoc />
    public bool IsDisposeComplete => Volatile.Read(ref _disposed) == 2;

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
      ThrowIfDisposed();

      DriverContext preparedContext = null;
      var reserved = false;
      var initStarted = false;
      try
      {
        await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
          ThrowIfDisposed();

          preparedContext = PrepareContext(driverId, context);
          ThrowIfDriverIdUnavailable(driverId, "Driver");
          ThrowIfDriverInstanceUnavailable(driver, "Driver");
          _reservedDriverIds.Add(driverId);
          _reservedDriverInstances.Add(driver);
          reserved = true;
        }
        finally
        {
          _registrationLock.Release();
        }

        initStarted = true;
        await driver.InitializeAsync(preparedContext, cancellationToken).ConfigureAwait(false);

        await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
          _reservedDriverIds.Remove(driverId);
          _reservedDriverInstances.Remove(driver);
          reserved = false;
          ThrowIfDisposed();

          var registration = new DriverRegistration
          {
            Driver = driver,
            Context = preparedContext,
            Type = driver.Type,
            Runtime = driver.Runtime
          };

          if (!_drivers.TryAdd(driverId, registration))
            throw new DriverException($"Driver '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);

          _registrationOrder.Add(driverId);
          SetDefaultIfFirst(driverId);
        }
        finally
        {
          _registrationLock.Release();
        }
      }
      catch (Exception ex)
      {
        if (reserved)
          await RollbackReservationAsync(driverId, driver).ConfigureAwait(false);
        if (initStarted)
        {
          await DisposeDriverSafelyAsync(driver, _logger).ConfigureAwait(false);
          MarkFailureDisposedInstance(ex);
        }
        throw;
      }
    }

    /// <summary>
    /// Unregisters a driver or driver pack.
    /// </summary>
    public void Unregister(string driverId)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      Task.Run(() => UnregisterAsync(driverId)).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string driverId, CancellationToken cancellationToken = default)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      DriverRegistration driver = null;
      DriverPackRegistration pack = null;

      await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        ThrowIfDisposed();
        var removedDriver = _drivers.TryRemove(driverId, out driver);
        var removedPack = _driverPacks.TryRemove(driverId, out pack);
        if (!removedDriver && !removedPack)
          throw CreateDriverNotFoundException(driverId);

        _registrationOrder.Remove(driverId);

        lock (_defaultDriverLock)
        {
          if (string.Equals(_defaultDriverId, driverId, StringComparison.Ordinal))
            _defaultDriverId = FirstRegisteredStillPresent();
        }
      }
      finally
      {
        _registrationLock.Release();
      }

      // Removal has committed under the lock, so we now own disposing these instances. Dispose within
      // the budget only: honoring the caller token here would let external cancellation abandon a
      // removed driver both undisposed and uncounted. Matches DisposeAsync.
      var deadline = DateTimeOffset.UtcNow + DisposeBudget;
      if (driver != null &&
          !await DisposeDriverWithinBudgetAsync(
              driver.Driver, _logger, driverId, Remaining(deadline))
              .ConfigureAwait(false))
        Interlocked.Increment(ref _abandonedDriverCount);
      if (pack != null &&
          !await DisposeDriverPackWithinBudgetAsync(
              pack.DriverPack, _logger, driverId, Remaining(deadline))
              .ConfigureAwait(false))
        Interlocked.Increment(ref _abandonedDriverCount);
    }

    /// <summary>
    /// Gets a driver by ID.
    /// </summary>
    public IDriver GetDriver(string driverId)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      if (!_drivers.TryGetValue(driverId, out var registration))
      {
        throw CreateDriverNotFoundException(driverId);
      }

      return registration.Driver;
    }

    /// <summary>
    /// Tries to get a driver by ID.
    /// </summary>
    public bool TryGetDriver(string driverId, [NotNullWhen(true)] out IDriver? driver)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
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
      ThrowIfDisposed();

      DriverContext preparedContext = null;
      var reserved = false;
      var initStarted = false;
      try
      {
        await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
          ThrowIfDisposed();

          preparedContext = PrepareContext(driverId, context);
          ThrowIfDriverIdUnavailable(driverId, "Driver pack");
          ThrowIfDriverInstanceUnavailable(driverPack, "Driver pack");
          _reservedDriverIds.Add(driverId);
          _reservedDriverInstances.Add(driverPack);
          reserved = true;
        }
        finally
        {
          _registrationLock.Release();
        }

        initStarted = true;
        await driverPack.InitializeAsync(preparedContext, cancellationToken).ConfigureAwait(false);

        await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
          _reservedDriverIds.Remove(driverId);
          _reservedDriverInstances.Remove(driverPack);
          reserved = false;
          ThrowIfDisposed();

          var registration = new DriverPackRegistration
          {
            DriverPack = driverPack,
            Context = preparedContext,
            Type = driverPack.Type,
            Runtime = driverPack.Runtime
          };

          if (!_driverPacks.TryAdd(driverId, registration))
            throw new DriverException($"Driver pack '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);

          _registrationOrder.Add(driverId);
          SetDefaultIfFirst(driverId);
        }
        finally
        {
          _registrationLock.Release();
        }
      }
      catch (Exception ex)
      {
        if (reserved)
          await RollbackReservationAsync(driverId, driverPack).ConfigureAwait(false);
        if (initStarted)
        {
          await DisposeDriverPackSafelyAsync(driverPack, _logger).ConfigureAwait(false);
          MarkFailureDisposedInstance(ex);
        }
        throw;
      }
    }

    /// <summary>
    /// Gets a driver pack by ID.
    /// </summary>
    public IDriverPack GetDriverPack(string driverId)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      if (!_driverPacks.TryGetValue(driverId, out var registration))
      {
        throw CreateDriverNotFoundException(driverId);
      }

      return registration.DriverPack;
    }

    /// <summary>
    /// Tries to get a driver pack by ID.
    /// </summary>
    public bool TryGetDriverPack(string driverId, [NotNullWhen(true)] out IDriverPack? driverPack)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
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
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      return _driverPacks.ContainsKey(driverId);
    }

    #endregion

    #region Context and Status

    /// <summary>
    /// Gets the driver context for a driver or driver pack.
    /// </summary>
    public DriverContext GetContext(string driverId)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      if (_drivers.TryGetValue(driverId, out var driverReg))
      {
        return driverReg.Context;
      }

      if (_driverPacks.TryGetValue(driverId, out var packReg))
      {
        return packReg.Context;
      }

      throw CreateDriverNotFoundException(driverId);
    }

    /// <summary>
    /// Checks if a driver or driver pack is registered.
    /// </summary>
    public bool IsRegistered(string driverId)
    {
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      return _drivers.ContainsKey(driverId) || _driverPacks.ContainsKey(driverId);
    }

    /// <summary>
    /// Gets all registered driver IDs (both drivers and driver packs).
    /// </summary>
    public IReadOnlyList<string> GetAllDriverIds()
    {
      ThrowIfDisposed();
      return [.. _drivers.Keys, .. _driverPacks.Keys];
    }

    #endregion

    #region Filtering

    /// <summary>
    /// Gets drivers and driver packs by type.
    /// </summary>
    public IReadOnlyList<string> GetDriversByType(DriverType driverType)
    {
      ThrowIfDisposed();
      var fromDrivers = _drivers
          .Where(kvp => kvp.Value.Type == driverType)
          .Select(kvp => kvp.Key);

      var fromPacks = _driverPacks
          .Where(kvp => kvp.Value.Type == driverType)
          .Select(kvp => kvp.Key);

      return [.. fromDrivers, .. fromPacks];
    }

    /// <summary>
    /// Gets drivers and driver packs by runtime.
    /// </summary>
    public IReadOnlyList<string> GetDriversByRuntime(RuntimeType runtime)
    {
      ThrowIfDisposed();
      var fromDrivers = _drivers
          .Where(kvp => kvp.Value.Runtime == runtime)
          .Select(kvp => kvp.Key);

      var fromPacks = _driverPacks
          .Where(kvp => kvp.Value.Runtime == runtime)
          .Select(kvp => kvp.Key);

      return [.. fromDrivers, .. fromPacks];
    }

    #endregion

    #region Default Driver

    /// <summary>
    /// Gets the default driver ID.
    /// If the current default is unregistered, the first registered driver still present becomes default.
    /// </summary>
    public string GetDefaultDriverId()
    {
      ThrowIfDisposed();
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
      ThrowIfDriverIdInvalid(driverId);
      ThrowIfDisposed();
      lock (_defaultDriverLock)
      {
        // Check inside lock to prevent TOCTOU race where another thread
        // could unregister the driver between check and set.
        if (!IsRegistered(driverId))
          throw CreateDriverNotFoundException(driverId);

        _defaultDriverId = driverId;
      }
    }

    #endregion

  }
}
