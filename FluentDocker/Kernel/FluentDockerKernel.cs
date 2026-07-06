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

      ThrowIfInvalidDriverId(driverId);
      if (string.IsNullOrEmpty(driverId))
        throw new InvalidOperationException("No default driver configured. Register a default driver or pass an explicit driver ID.");

      if (TryResolveCore(driverId, interfaceType, out var resolved))
        return resolved;
      throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
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
    /// Tries to get a driver component interface. Returns false instead of throwing
    /// when the interface is not supported. A missing driver still throws.
    /// </summary>
    public bool TrySysCtl<T>(string driverId, [NotNullWhen(true)] out T? instance) where T : class
    {
      ThrowIfDisposed();
      instance = null;

      ThrowIfInvalidDriverId(driverId);
      if (string.IsNullOrEmpty(driverId))
        throw new InvalidOperationException("No default driver configured. Register a default driver or pass an explicit driver ID.");

      if (TryResolveCore(driverId, typeof(T), out var resolved))
      {
        instance = (T)resolved;
        return true;
      }

      return false;
      // DriverNotFoundException intentionally propagates -
      // a missing driver is a hard error, not a "not supported" case
    }

    #endregion

    #region Driver Access

    /// <summary>
    /// Gets the underlying driver instance.
    /// </summary>
    public IDriver GetDriver(string driverId)
    {
      ThrowIfDisposed();
      return _registry.GetDriver(driverId);
    }

    /// <summary>
    /// Gets the underlying driver pack instance.
    /// </summary>
    public IDriverPack GetDriverPack(string driverId)
    {
      ThrowIfDisposed();
      return _registry.GetDriverPack(driverId);
    }

    /// <summary>
    /// Checks if a driver ID refers to a driver pack.
    /// </summary>
    public bool IsDriverPack(string driverId)
    {
      ThrowIfDisposed();
      return _registry.IsDriverPack(driverId);
    }

    #endregion

    #region Driver Registration

    /// <summary>
    /// Registers a driver.
    /// </summary>
    public async Task RegisterDriverAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      await _registry.RegisterAsync(driverId, driver, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a driver pack.
    /// </summary>
    public async Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      await _registry.RegisterDriverPackAsync(driverId, driverPack, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unregisters and disposes a driver or driver pack. Clearing the default
    /// driver unregisters the default without selecting a replacement.
    /// </summary>
    public void UnregisterDriver(string driverId)
    {
      ThrowIfDisposed();
      _registry.Unregister(driverId);
    }

    /// <summary>
    /// Asynchronously unregisters and disposes a driver or driver pack. Clearing
    /// the default driver unregisters the default without selecting a replacement.
    /// </summary>
    /// <param name="driverId">Driver identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnregisterDriverAsync(string driverId, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      await _registry.UnregisterAsync(driverId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if a driver or driver pack is registered.
    /// </summary>
    public bool IsDriverRegistered(string driverId)
    {
      ThrowIfDisposed();
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
        [NotNullWhen(true)] out object? resolved)
    {
      if (_registry.TryGetDriverPack(driverId, out var driverPack))
      {
        if (driverPack.TryResolve(interfaceType, out resolved)
            && interfaceType.IsInstanceOfType(resolved))
          return true;

        try
        {
          resolved = driverPack.SysCtl(driverId, interfaceType);
          if (interfaceType.IsInstanceOfType(resolved))
            return true;
        }
        catch (InterfaceNotSupportedException)
        {
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

      throw new DriverNotFoundException(driverId);
    }

    private static void ThrowIfInvalidDriverId(string driverId)
    {
      if (driverId != null && driverId.Length > 0 && string.IsNullOrWhiteSpace(driverId))
        throw new ArgumentException("Driver ID cannot be empty or whitespace.", nameof(driverId));
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
        if (_registry is IAsyncDisposable asyncDisposable)
          await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (_registry is IDisposable disposable)
          disposable.Dispose();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Kernel DisposeAsync cleanup failed");
      }
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
