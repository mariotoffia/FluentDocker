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
    private readonly List<string> _registrationOrder = [];
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DriverRegistry> _logger;
    private string _defaultDriverId;
    private readonly object _defaultDriverLock = new object();
    private int _disposed;

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

    internal bool IsDisposeComplete => Volatile.Read(ref _disposed) == 2;

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

      DriverContext preparedContext;
      await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        ThrowIfDisposed();

        ThrowIfDriverIdUnavailable(driverId, "Driver");
        _reservedDriverIds.Add(driverId);
        preparedContext = PrepareContext(driverId, context);
      }
      finally
      {
        _registrationLock.Release();
      }

      try
      {
        await driver.InitializeAsync(preparedContext, cancellationToken).ConfigureAwait(false);

        await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
          _reservedDriverIds.Remove(driverId);
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
      catch
      {
        await RollbackReservationAsync(driverId).ConfigureAwait(false);
        await DisposeDriverSafelyAsync(driver, _logger).ConfigureAwait(false);
        throw;
      }
    }

    /// <summary>
    /// Unregisters a driver or driver pack.
    /// </summary>
    public void Unregister(string driverId)
    {
      ThrowIfDisposed();
      Task.Run(() => UnregisterAsync(driverId)).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string driverId, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      DriverRegistration driver = null;
      DriverPackRegistration pack = null;

      await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        ThrowIfDisposed();
        _drivers.TryRemove(driverId, out driver);
        _driverPacks.TryRemove(driverId, out pack);
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

      if (driver != null)
        await DisposeDriverSafelyAsync(driver.Driver, _logger).ConfigureAwait(false);
      if (pack != null)
        await DisposeDriverPackSafelyAsync(pack.DriverPack, _logger).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a driver by ID.
    /// </summary>
    public IDriver GetDriver(string driverId)
    {
      ThrowIfDisposed();
      if (!_drivers.TryGetValue(driverId, out var registration))
      {
        throw new DriverNotFoundException(driverId);
      }

      return registration.Driver;
    }

    /// <summary>
    /// Tries to get a driver by ID.
    /// </summary>
    public bool TryGetDriver(string driverId, [NotNullWhen(true)] out IDriver? driver)
    {
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

      DriverContext preparedContext;
      await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        ThrowIfDisposed();

        ThrowIfDriverIdUnavailable(driverId, "Driver pack");
        _reservedDriverIds.Add(driverId);
        preparedContext = PrepareContext(driverId, context);
      }
      finally
      {
        _registrationLock.Release();
      }

      try
      {
        await driverPack.InitializeAsync(preparedContext, cancellationToken).ConfigureAwait(false);

        await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
          _reservedDriverIds.Remove(driverId);
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
      catch
      {
        await RollbackReservationAsync(driverId).ConfigureAwait(false);
        await DisposeDriverPackSafelyAsync(driverPack, _logger).ConfigureAwait(false);
        throw;
      }
    }

    /// <summary>
    /// Gets a driver pack by ID.
    /// </summary>
    public IDriverPack GetDriverPack(string driverId)
    {
      ThrowIfDisposed();
      if (!_driverPacks.TryGetValue(driverId, out var registration))
      {
        throw new DriverNotFoundException(driverId);
      }

      return registration.DriverPack;
    }

    /// <summary>
    /// Tries to get a driver pack by ID.
    /// </summary>
    public bool TryGetDriverPack(string driverId, [NotNullWhen(true)] out IDriverPack? driverPack)
    {
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
      ThrowIfDisposed();
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
      ThrowIfDisposed();
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
        _defaultDriverId ??= driverId;
      }
    }

    private string FirstRegisteredStillPresent()
    {
      return _registrationOrder.FirstOrDefault(id =>
          _drivers.ContainsKey(id) || _driverPacks.ContainsKey(id));
    }

    private DriverContext PrepareContext(string driverId, DriverContext context)
    {
      if (!string.IsNullOrEmpty(context.DriverId)
          && !string.Equals(context.DriverId, driverId, StringComparison.Ordinal))
        throw new ArgumentException(
            $"Driver context ID '{context.DriverId}' does not match registration ID '{driverId}'.",
            nameof(context));

      var loggerFactory = IsNullLoggerFactory(context.LoggerFactory)
          ? _loggerFactory
          : context.LoggerFactory;
      return context.CloneWith(driverId, loggerFactory);
    }

    private void ThrowIfDriverIdUnavailable(string driverId, string kind)
    {
      if (_reservedDriverIds.Contains(driverId) ||
          _drivers.ContainsKey(driverId) ||
          _driverPacks.ContainsKey(driverId))
        throw new DriverException($"{kind} '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);
    }

    private async Task RollbackReservationAsync(string driverId)
    {
      await _registrationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
      try
      {
        _reservedDriverIds.Remove(driverId);
      }
      finally
      {
        _registrationLock.Release();
      }
    }

    private static bool IsNullLoggerFactory(ILoggerFactory loggerFactory)
    {
      return loggerFactory == null
             || ReferenceEquals(loggerFactory, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
