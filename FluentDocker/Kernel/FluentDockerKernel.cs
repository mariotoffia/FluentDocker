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
    public class FluentDockerKernel : ISysCtl, IDisposable
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
        /// SysCtl - System control interface for accessing drivers.
        /// Gets a driver component interface by driver ID and component type.
        /// If the driverId maps to an IDriverPack, delegates to the pack first.
        /// </summary>
        /// <typeparam name="T">Driver component interface (IContainerDriver, IImageDriver, etc.)</typeparam>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver component instance</returns>
        /// <exception cref="DriverNotFoundException">If driver not found</exception>
        /// <exception cref="InterfaceNotSupportedException">If driver doesn't implement interface</exception>
        public T SysCtl<T>(string driverId) where T : class
        {
            ThrowIfDisposed();

            // First check if driverId refers to a driver pack
            if (_registry.TryGetDriverPack(driverId, out var driverPack))
            {
                // Delegate to the driver pack's SysCtl
                return driverPack.SysCtl<T>(driverId);
            }

            // Fall back to regular driver resolution
            if (_registry.TryGetDriver(driverId, out var driver))
            {
                if (driver is T typedDriver)
                {
                    return typedDriver;
                }

                throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
            }

            throw new DriverNotFoundException(driverId);
        }

        /// <summary>
        /// SysCtl - System control interface for accessing drivers.
        /// Gets a driver component interface by driver ID and component enum.
        /// If the driverId maps to an IDriverPack, delegates to the pack first.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="component">Driver component</param>
        /// <returns>Driver component instance</returns>
        public object SysCtl(string driverId, DriverComponent component)
        {
            ThrowIfDisposed();

            // First check if driverId refers to a driver pack
            if (_registry.TryGetDriverPack(driverId, out var driverPack))
            {
                // Delegate to the driver pack's SysCtl
                return driverPack.SysCtl(driverId, component);
            }

            // Fall back to regular driver resolution via component switch
            return component switch
            {
                DriverComponent.Container => ResolveFromDriver<IContainerDriver>(driverId),
                DriverComponent.Image => ResolveFromDriver<IImageDriver>(driverId),
                DriverComponent.Network => ResolveFromDriver<INetworkDriver>(driverId),
                DriverComponent.Volume => ResolveFromDriver<IVolumeDriver>(driverId),
                DriverComponent.System => ResolveFromDriver<ISystemDriver>(driverId),
                DriverComponent.Compose => ResolveFromDriver<IComposeDriver>(driverId),
                _ => throw new ArgumentException($"Unknown component: {component}", nameof(component))
            };
        }

        /// <summary>
        /// Resolves a driver interface from a regular driver (not a pack).
        /// </summary>
        private T ResolveFromDriver<T>(string driverId) where T : class
        {
            var driver = _registry.GetDriver(driverId);

            if (driver is T typedDriver)
            {
                return typedDriver;
            }

            throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
        }

        #endregion

        #region Driver Access

        /// <summary>
        /// Gets the underlying driver instance.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver instance</returns>
        public IDriver GetDriver(string driverId)
        {
            ThrowIfDisposed();
            return _registry.GetDriver(driverId);
        }

        /// <summary>
        /// Gets the underlying driver pack instance.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver pack instance</returns>
        public IDriverPack GetDriverPack(string driverId)
        {
            ThrowIfDisposed();
            return _registry.GetDriverPack(driverId);
        }

        /// <summary>
        /// Checks if a driver ID refers to a driver pack.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>True if the ID refers to a driver pack</returns>
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
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="driver">Driver instance</param>
        /// <param name="context">Driver context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task RegisterDriverAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _registry.RegisterAsync(driverId, driver, context, cancellationToken);
        }

        /// <summary>
        /// Registers a driver pack.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="driverPack">Driver pack instance</param>
        /// <param name="context">Driver context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task RegisterDriverPackAsync(string driverId, IDriverPack driverPack, DriverContext context, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _registry.RegisterDriverPackAsync(driverId, driverPack, context, cancellationToken);
        }

        /// <summary>
        /// Unregisters a driver or driver pack.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        public void UnregisterDriver(string driverId)
        {
            ThrowIfDisposed();
            _registry.Unregister(driverId);
        }

        /// <summary>
        /// Checks if a driver or driver pack is registered.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>True if registered</returns>
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
        /// <param name="driverId">Driver identifier</param>
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

        #region IDisposable

        /// <summary>
        /// Disposes the kernel and all registered drivers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Unregister all drivers and driver packs
            var driverIds = _registry.GetAllDriverIds();
            foreach (var driverId in driverIds)
            {
                try
                {
                    _registry.Unregister(driverId);
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }
        }

        #endregion
    }
}
