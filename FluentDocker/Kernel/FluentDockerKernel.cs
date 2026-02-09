using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

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
        private bool _disposed;

        /// <summary>
        /// Creates a new kernel with the default registry.
        /// </summary>
        public FluentDockerKernel() : this(new DriverRegistry())
        {
        }

        /// <summary>
        /// Creates a new kernel with a custom registry.
        /// </summary>
        /// <param name="registry">Driver registry</param>
        public FluentDockerKernel(IDriverRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        #region ISysCtl Implementation

        /// <summary>
        /// Resolves a driver interface by driver ID and runtime type.
        /// This is the unified resolution path used by all other SysCtl overloads.
        /// Resolution order:
        /// 1. If driver pack implements IDriverInterfaceResolver, ask it.
        /// 2. Else delegate to the driver pack's ISysCtl.
        /// 3. If regular driver implements IDriverInterfaceResolver, ask it.
        /// 4. Else fallback to direct cast (driver is T).
        /// </summary>
        public object SysCtl(string driverId, Type interfaceType)
        {
            ThrowIfDisposed();

            // First check if driverId refers to a driver pack
            if (_registry.TryGetDriverPack(driverId, out var driverPack))
            {
                // If the pack is an IDriverInterfaceResolver, use it
                if (driverPack is IDriverInterfaceResolver resolver &&
                    resolver.TryResolve(interfaceType, out var resolved))
                    return resolved;

                // Fallback: delegate to pack's type-based SysCtl
                return driverPack.SysCtl(driverId, interfaceType);
            }

            // Fall back to regular driver resolution
            if (_registry.TryGetDriver(driverId, out var driver))
            {
                // If the driver is an IDriverInterfaceResolver, use it
                if (driver is IDriverInterfaceResolver driverResolver &&
                    driverResolver.TryResolve(interfaceType, out var driverResolved))
                    return driverResolved;

                // Direct cast check
                if (interfaceType.IsInstanceOfType(driver))
                    return driver;

                throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
            }

            throw new DriverNotFoundException(driverId);
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
        public bool TrySysCtl<T>(string driverId, out T instance) where T : class
        {
            ThrowIfDisposed();
            instance = null;

            try
            {
                instance = SysCtl<T>(driverId);
                return true;
            }
            catch (InterfaceNotSupportedException)
            {
                return false;
            }
            // DriverNotFoundException intentionally propagates -
            // a missing driver is a hard error, not a "not supported" case
        }

        /// <summary>
        /// Gets a driver component interface by driver ID and component enum.
        /// Maps the enum to the well-known interface type and delegates.
        /// </summary>
        public object SysCtl(string driverId, DriverComponent component)
        {
            ThrowIfDisposed();
            var interfaceType = ComponentToType(component);
            return SysCtl(driverId, interfaceType);
        }

        private static Type ComponentToType(DriverComponent component) => component switch
        {
            DriverComponent.Container => typeof(IContainerDriver),
            DriverComponent.Image => typeof(IImageDriver),
            DriverComponent.Network => typeof(INetworkDriver),
            DriverComponent.Volume => typeof(IVolumeDriver),
            DriverComponent.System => typeof(ISystemDriver),
            DriverComponent.Compose => typeof(IComposeDriver),
            _ => throw new ArgumentException($"Unknown component: {component}", nameof(component))
        };

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
            await _registry.RegisterAsync(driverId, driver, context, cancellationToken);
        }

        /// <summary>
        /// Registers a driver pack.
        /// </summary>
        public async Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _registry.RegisterDriverPackAsync(driverId, driverPack, context, cancellationToken);
        }

        /// <summary>
        /// Unregisters a driver or driver pack.
        /// </summary>
        public void UnregisterDriver(string driverId)
        {
            ThrowIfDisposed();
            _registry.Unregister(driverId);
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
        /// Creates a new kernel builder.
        /// </summary>
        public static IKernelBuilder Create()
        {
            return new KernelBuilder();
        }

        #endregion

        #region Private Helpers

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FluentDockerKernel));
            }
        }

        #endregion

        #region IAsyncDisposable / IDisposable

        /// <summary>
        /// Asynchronously disposes the kernel and all registered driver packs / drivers.
        /// Driver packs implementing <see cref="IAsyncDisposable"/> are disposed asynchronously;
        /// those implementing only <see cref="IDisposable"/> are disposed synchronously.
        /// Regular drivers follow the same pattern.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            var driverIds = _registry.GetAllDriverIds();
            foreach (var driverId in driverIds)
            {
                try
                {
                    // Dispose driver packs
                    if (_registry.TryGetDriverPack(driverId, out var driverPack))
                    {
                        if (driverPack is IAsyncDisposable asyncDisposable)
                            await asyncDisposable.DisposeAsync();
                        else if (driverPack is IDisposable disposable)
                            disposable.Dispose();
                    }

                    // Dispose regular drivers
                    if (_registry.TryGetDriver(driverId, out var driver))
                    {
                        if (driver is IAsyncDisposable asyncDisposableDriver)
                            await asyncDisposableDriver.DisposeAsync();
                        else if (driver is IDisposable disposableDriver)
                            disposableDriver.Dispose();
                    }

                    _registry.Unregister(driverId);
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }
        }

        /// <summary>
        /// Synchronously disposes the kernel and all registered driver packs / drivers.
        /// Delegates to <see cref="DisposeAsync"/> and blocks until completion.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        #endregion
    }
}
