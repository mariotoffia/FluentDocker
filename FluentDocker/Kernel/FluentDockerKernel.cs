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
    /// </summary>
    public class FluentDockerKernel : IDisposable
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

        /// <summary>
        /// SysCtl - System control interface for accessing drivers.
        /// Gets a driver component interface by driver ID and component type.
        /// </summary>
        /// <typeparam name="T">Driver component interface (IContainerDriver, IImageDriver, etc.)</typeparam>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>Driver component instance</returns>
        /// <exception cref="DriverNotFoundException">If driver not found</exception>
        /// <exception cref="InterfaceNotSupportedException">If driver doesn't implement interface</exception>
        public T SysCtl<T>(string driverId) where T : class
        {
            ThrowIfDisposed();

            var driver = _registry.GetDriver(driverId);

            if (driver is T typedDriver)
            {
                return typedDriver;
            }

            throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
        }

        /// <summary>
        /// SysCtl - System control interface for accessing drivers.
        /// Gets a driver component interface by driver ID and component enum.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="component">Driver component</param>
        /// <returns>Driver component instance</returns>
        public object SysCtl(string driverId, DriverComponent component)
        {
            return component switch
            {
                DriverComponent.Container => SysCtl<IContainerDriver>(driverId),
                DriverComponent.Image => SysCtl<IImageDriver>(driverId),
                DriverComponent.Network => SysCtl<INetworkDriver>(driverId),
                DriverComponent.Volume => SysCtl<IVolumeDriver>(driverId),
                DriverComponent.System => SysCtl<ISystemDriver>(driverId),
                _ => throw new ArgumentException($"Unknown component: {component}", nameof(component))
            };
        }

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
        /// Unregisters a driver.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        public void UnregisterDriver(string driverId)
        {
            ThrowIfDisposed();
            _registry.Unregister(driverId);
        }

        /// <summary>
        /// Checks if a driver is registered.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <returns>True if registered</returns>
        public bool IsDriverRegistered(string driverId)
        {
            ThrowIfDisposed();
            return _registry.IsRegistered(driverId);
        }

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

        /// <summary>
        /// Gets the driver registry.
        /// </summary>
        internal IDriverRegistry Registry => _registry;

        /// <summary>
        /// Creates a new kernel builder.
        /// </summary>
        public static IKernelBuilder Create()
        {
            return new KernelBuilder();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FluentDockerKernel));
            }
        }

        /// <summary>
        /// Disposes the kernel and all registered drivers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Unregister all drivers
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
    }
}
