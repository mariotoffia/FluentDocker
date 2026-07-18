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
  public partial class FluentDockerKernel : ISysCtl, IAsyncDisposable, IDisposable
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
    /// 1. Registered driver PACK: the pack's IDriverInterfaceResolver.TryResolve is the single
    ///    resolution path — packs never fall back to a direct cast (KRN-MAJ-7 removed the
    ///    pack-level ISysCtl delegation).
    /// 2. Plain driver: if it implements IDriverInterfaceResolver, ask it; otherwise fall back
    ///    to a direct cast (driver is T).
    /// </summary>
    public object SysCtl(string driverId, Type interfaceType)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(interfaceType);

      driverId = ResolveDriverIdOrDefault(driverId);

      if (TryResolveCore(driverId, interfaceType, out var resolved, out var unsupportedCause, out var mismatchedType))
        return resolved;
      var interfaceName = TypeNameFormatter.Format(interfaceType);
      if (mismatchedType != null)
        throw new InterfaceNotSupportedException(driverId, interfaceName, mismatchedType);
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
    /// unsupported. Missing drivers, disposal/cancellation, I/O failures, and
    /// unexpected resolver faults still throw.
    /// </summary>
    public bool TrySysCtl<T>(string driverId, [NotNullWhen(true)] out T? instance) where T : class
    {
      ThrowIfDisposed();
      instance = null;
      driverId = ResolveDriverIdOrDefault(driverId);

      // A genuinely unsupported interface makes TryResolveCore return false (→ false here);
      // real faults (broken resolver, fallback fault, missing driver, I/O, cancellation) throw.
      if (TryResolveCore(driverId, typeof(T), out var resolved, out _, out _))
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
    /// Gets the default driver ID, or <c>null</c> when none is set.
    /// </summary>
    public string? DefaultDriverId
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

    /// <inheritdoc />
    public async Task<DriverCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken cancellationToken = default)
    {
      var resolved = ResolveDriverIdOrDefault(driverId);
      return IsDriverPack(resolved)
          ? await GetDriverPack(resolved).GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false)
          : await GetDriver(resolved).GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(string driverId, CancellationToken cancellationToken = default)
    {
      var resolved = ResolveDriverIdOrDefault(driverId);
      return IsDriverPack(resolved)
          ? await GetDriverPack(resolved).IsHealthyAsync(cancellationToken).ConfigureAwait(false)
          : await GetDriver(resolved).IsHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Registry Access

    /// <summary>
    /// Gets the driver registry.
    /// </summary>
    internal IDriverRegistry Registry => _registry;

    /// <summary>
    /// Number of driver/pack instances abandoned because their disposal exceeded the teardown
    /// budget. A non-zero value after disposal means OS processes/containers may have leaked.
    /// Exposed so leaks are observable without a downcast to the concrete registry (KRN-MAJ-2).
    /// </summary>
    public int AbandonedDriverCount => _registry.AbandonedDriverCount;

    /// <summary>
    /// <c>true</c> once the kernel's registry disposal pass has completed. This does <em>not</em>
    /// mean every driver/pack was cleanly disposed — budget-exhausted instances are abandoned;
    /// also check <see cref="AbandonedDriverCount"/> to detect that partial cleanup.
    /// </summary>
    public bool IsDisposeComplete => _registry.IsDisposeComplete;

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

    // TryResolveCore and its resolution-only helpers live in FluentDockerKernel.Resolution.cs
    // (kept as a partial-class split so this file stays under the 500-line limit).

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

    #endregion

    #region IAsyncDisposable / IDisposable

    /// <summary>
    /// Asynchronously disposes the kernel and all registered driver packs / drivers.
    /// Driver packs implementing <see cref="IAsyncDisposable"/> are disposed asynchronously;
    /// those implementing only <see cref="IDisposable"/> are disposed synchronously.
    /// Regular drivers follow the same pattern.
    /// </summary>
    /// <remarks>
    /// Concurrent dispose calls may both enter registry disposal; the second caller can wait
    /// behind the registry's disposal budget. Serialize disposal when a strict caller timeout is
    /// required.
    /// </remarks>
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

      if (!_registry.IsDisposeComplete)
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
