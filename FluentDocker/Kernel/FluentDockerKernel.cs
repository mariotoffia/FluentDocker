using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// FluentDocker kernel - manages driver instances and provides SysCtl() access.
  /// Non-singleton in v3.0.0 - can have multiple kernel instances.
  /// Implements ISysCtl for unified driver component access.
  /// </summary>
  public class FluentDockerKernel : ISysCtl, IAsyncDisposable, IDisposable
  {
    private readonly IDriverRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FluentDockerKernel> _logger;
    private int _disposed; // 0=not disposed, 1=disposed; use Interlocked for atomic check-and-set

    /// <summary>
    /// Creates a new kernel with a registry and the consumer-supplied logger factory.
    /// Both parameters are required and must not be null.
    /// </summary>
    /// <param name="registry">Driver registry that owns the driver/pack lifecycle.</param>
    /// <param name="loggerFactory">Logger factory used to create per-type loggers
    /// inside the kernel and propagated through <see cref="DriverContext"/> to packs.
    /// Pass <see cref="Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance"/> to suppress logging.</param>
    public FluentDockerKernel(IDriverRegistry registry, ILoggerFactory loggerFactory)
    {
      ArgumentNullException.ThrowIfNull(registry);
      ArgumentNullException.ThrowIfNull(loggerFactory);
      _registry = registry;
      _loggerFactory = loggerFactory;
      _logger = loggerFactory.CreateLogger<FluentDockerKernel>();
    }

    /// <summary>
    /// Logger factory provided at construction. Builders, services, and resources
    /// constructed via this kernel use it to create their per-type loggers.
    /// </summary>
    public ILoggerFactory LoggerFactory => _loggerFactory;

    #region ISysCtl Implementation

    /// <summary>
    /// Resolves a driver interface by driver ID and runtime type.
    /// This is the unified resolution path used by all other SysCtl overloads.
    /// Resolution order:
    /// 1. Driver pack's IDriverInterfaceResolver.TryResolve.
    /// 2. Else delegate to the driver pack's ISysCtl.
    /// 3. If regular driver implements IDriverInterfaceResolver, ask it.
    /// 4. Else fallback to direct cast (driver is T).
    /// </summary>
    public object SysCtl(string driverId, Type interfaceType)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(interfaceType);

      driverId = ResolveDriverIdOrDefault(driverId);

      if (TryResolveCore(driverId, interfaceType, out var resolved, out var unsupportedCause))
        return resolved;
      var interfaceName = TypeNameFormatter.Format(interfaceType);
      throw unsupportedCause == null
          ? new InterfaceNotSupportedException(driverId, interfaceName)
          : new InterfaceNotSupportedException(driverId, interfaceName, unsupportedCause);
    }

    /// <summary>
    /// Gets a driver component interface by driver ID and generic type.
    /// Delegates to the unified type-based resolution.
    /// </summary>
    public T SysCtl<T>(string driverId) where T : class
    {
      return (T)SysCtl(driverId, typeof(T));
    }

    /// <summary>
    /// Tries to get a driver component interface. Returns false when the interface is
    /// unsupported. Missing drivers, disposal/cancellation, and unexpected fallback
    /// faults still throw.
    /// </summary>
    public bool TrySysCtl<T>(string driverId, [NotNullWhen(true)] out T? instance) where T : class
    {
      ThrowIfDisposed();
      instance = null;
      driverId = ResolveDriverIdOrDefault(driverId);

      if (TryResolveCore(driverId, typeof(T), out var resolved, out _))
      {
        instance = (T)resolved;
        return true;
      }

      return false;
    }

    #endregion

    #region Driver Access

    /// <summary>
    /// Gets the underlying driver instance.
    /// </summary>
    public IDriver GetDriver(string driverId)
    {
      ThrowIfDisposed();
      driverId = ResolveDriverIdOrDefault(driverId);
      return _registry.GetDriver(driverId);
    }

    /// <summary>
    /// Gets the underlying driver pack instance.
    /// </summary>
    public IDriverPack GetDriverPack(string driverId)
    {
      ThrowIfDisposed();
      driverId = ResolveDriverIdOrDefault(driverId);
      return _registry.GetDriverPack(driverId);
    }

    /// <summary>
    /// Checks if a driver ID refers to a driver pack.
    /// </summary>
    public bool IsDriverPack(string driverId)
    {
      ThrowIfDisposed();
      driverId = ResolveDriverIdOrDefault(driverId);
      return _registry.IsDriverPack(driverId);
    }

    #endregion

    #region Driver Registration

    /// <summary>
    /// Registers a driver.
    /// </summary>
    /// <remarks>On failure after acceptance begins, the registry disposes the supplied instance; do not reuse or re-dispose it.</remarks>
    public async Task RegisterDriverAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      await _registry.RegisterAsync(driverId, driver, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a driver pack.
    /// </summary>
    /// <remarks>On failure after acceptance begins, the registry disposes the supplied instance; do not reuse or re-dispose it.</remarks>
    public async Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      await _registry.RegisterDriverPackAsync(driverId, driverPack, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unregisters and disposes a driver or driver pack. If the removed driver was
    /// the default, the earliest-registered driver still present becomes the default (null if none remain).
    /// </summary>
    public void UnregisterDriver(string driverId)
    {
      ThrowIfDisposed();
      driverId = RequireDriverId(driverId);
      _registry.Unregister(driverId);
    }

    /// <summary>
    /// Asynchronously unregisters and disposes a driver or driver pack. If the removed driver was
    /// the default, the earliest-registered driver still present becomes the default (null if none remain).
    /// </summary>
    /// <param name="driverId">Driver identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnregisterDriverAsync(string driverId, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      driverId = RequireDriverId(driverId);
      await _registry.UnregisterAsync(driverId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if a driver or driver pack is registered.
    /// </summary>
    public bool IsDriverRegistered(string driverId)
    {
      ThrowIfDisposed();
      driverId = RequireDriverId(driverId);
      return _registry.IsRegistered(driverId);
    }

    #endregion

    #region Default Driver

    /// <summary>
    /// Gets the default driver ID.
    /// </summary>
    public string DefaultDriverId
    {
      get
      {
        ThrowIfDisposed();
        return _registry.GetDefaultDriverId();
      }
    }

    /// <summary>
    /// Sets the default driver.
    /// </summary>
    public void SetDefaultDriver(string driverId)
    {
      ThrowIfDisposed();
      driverId = RequireDriverId(driverId);
      _registry.SetDefaultDriver(driverId);
    }

    #endregion

    #region Registry Access

    /// <summary>
    /// Gets the driver registry.
    /// </summary>
    internal IDriverRegistry Registry => _registry;

    #endregion

    #region Builder

    /// <summary>
    /// Creates a new kernel builder with the consumer-supplied logger factory.
    /// </summary>
    /// <param name="loggerFactory">Logger factory; required. Pass
    /// <see cref="Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance"/>
    /// to suppress all logging from the constructed kernel and its drivers.</param>
    public static IKernelBuilder Create(ILoggerFactory loggerFactory)
    {
      return new KernelBuilder(loggerFactory);
    }

    /// <summary>
    /// Creates a new kernel builder with logging suppressed
    /// (<see cref="Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory"/>).
    /// A convenience for callers that do not need diagnostics; equivalent to
    /// <c>Create(NullLoggerFactory.Instance)</c>.
    /// </summary>
    public static IKernelBuilder Create() =>
        Create(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

    #endregion

    #region Private Helpers

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private bool TryResolveCore(
        string driverId,
        Type interfaceType,
        [NotNullWhen(true)] out object? resolved,
        out Exception? unsupportedCause)
    {
      unsupportedCause = null;
      if (_registry.TryGetDriverPack(driverId, out var driverPack))
      {
        if (driverPack.TryResolve(interfaceType, out resolved))
        {
          if (interfaceType.IsInstanceOfType(resolved))
            return true;
          LogTypeMismatch(driverPack, interfaceType, resolved);
        }

        try
        {
          resolved = driverPack.SysCtl(driverId, interfaceType);
          if (interfaceType.IsInstanceOfType(resolved))
            return true;
          LogTypeMismatch(driverPack, interfaceType, resolved);
        }
        catch (Exception ex)
        {
          if (ex is OperationCanceledException || ex is ObjectDisposedException)
            throw;

          if (ex is InterfaceNotSupportedException)
          {
            unsupportedCause = ex;
          }
          else
          {
            LogPackFallbackFailure(driverPack, interfaceType, ex);
            throw new InterfaceNotSupportedException(
                driverId, TypeNameFormatter.Format(interfaceType), ex);
          }
        }

        resolved = null;
        return false;
      }

      if (_registry.TryGetDriver(driverId, out var driver))
      {
        if (driver is IDriverInterfaceResolver driverResolver
            && driverResolver.TryResolve(interfaceType, out resolved)
            && interfaceType.IsInstanceOfType(resolved))
          return true;

        if (interfaceType.IsInstanceOfType(driver))
        {
          resolved = driver;
          return true;
        }

        resolved = null;
        return false;
      }

      throw new DriverNotFoundException(driverId, _registry.GetAllDriverIds());
    }

    private string ResolveDriverIdOrDefault(string driverId)
    {
      if (!string.IsNullOrWhiteSpace(driverId))
        return driverId;

      var defaultDriverId = _registry.GetDefaultDriverId();
      if (string.IsNullOrWhiteSpace(defaultDriverId))
        throw new InvalidOperationException("No default driver configured. Register a default driver or pass an explicit driver ID.");
      return defaultDriverId;
    }

    private static string RequireDriverId(string driverId)
    {
      if (string.IsNullOrWhiteSpace(driverId))
        throw new ArgumentException("Driver ID cannot be empty or whitespace.", nameof(driverId));
      return driverId;
    }

    private void LogTypeMismatch(IDriverPack driverPack, Type interfaceType, object resolved)
    {
      if (!_logger.IsEnabled(LogLevel.Debug))
        return;

      _logger.LogDebug(
          "Driver pack {DriverPackType} returned {ActualType} for requested interface {InterfaceType}",
          driverPack.GetType().FullName,
          resolved?.GetType().FullName ?? "<null>",
          TypeNameFormatter.Format(interfaceType));
    }

    private void LogPackFallbackFailure(IDriverPack driverPack, Type interfaceType, Exception ex)
    {
      _logger.LogWarning(
          ex,
          "Driver pack {DriverPackType} failed to resolve interface {InterfaceType}",
          driverPack.GetType().FullName,
          TypeNameFormatter.Format(interfaceType));
    }

    #endregion

    #region IAsyncDisposable / IDisposable

    /// <summary>
    /// Asynchronously disposes the kernel and all registered driver packs / drivers.
    /// Driver packs implementing <see cref="IAsyncDisposable"/> are disposed asynchronously;
    /// those implementing only <see cref="IDisposable"/> are disposed synchronously.
    /// Regular drivers follow the same pattern.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
      {
        await DisposeRegistrySafelyAsync().ConfigureAwait(false);
        return;
      }

      // Delegate disposal to the registry which owns the driver lifecycle.
      // This avoids double-disposal if both kernel and registry are disposed.
      await DisposeRegistrySafelyAsync().ConfigureAwait(false);

      GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeRegistrySafelyAsync()
    {
      try
      {
        await _registry.DisposeAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        if (ex is TimeoutException)
        {
          await RetryRegistryDisposeAfterTimeoutAsync(ex).ConfigureAwait(false);
          return;
        }

        _logger.LogWarning(ex, "Kernel DisposeAsync cleanup failed");
      }
    }

    private async ValueTask RetryRegistryDisposeAfterTimeoutAsync(Exception original)
    {
      try
      {
        await _registry.DisposeAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Kernel DisposeAsync cleanup retry failed");
      }

      if (_registry is DriverRegistry registry && !registry.IsDisposeComplete)
        _logger.LogError(original, "Kernel DisposeAsync cleanup did not complete after retry");
    }

    /// <summary>
    /// Synchronously disposes the kernel and all registered driver packs / drivers.
    /// Delegates to <see cref="DisposeAsync"/> via Task.Run to avoid deadlocks
    /// on single-threaded synchronization contexts (ASP.NET, WPF).
    /// Prefer <see cref="DisposeAsync"/> when possible.
    /// </summary>
#pragma warning disable CA1816
    public virtual void Dispose()
    {
      // DisposeAsync uses Interlocked.CompareExchange for atomic guard,
      // so this just delegates without a separate check.
      // Dispatched to the thread pool to avoid sync-over-async deadlocks.
      Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }
#pragma warning restore CA1816

    #endregion
  }
}
