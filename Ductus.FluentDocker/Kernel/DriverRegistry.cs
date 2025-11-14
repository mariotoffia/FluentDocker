using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Kernel
{
    /// <summary>
    /// Default implementation of the driver registry.
    /// </summary>
    public class DriverRegistry : IDriverRegistry
    {
        private readonly ConcurrentDictionary<string, DriverRegistration> _drivers = new ConcurrentDictionary<string, DriverRegistration>();
        private string _defaultDriverId;
        private readonly object _defaultDriverLock = new object();

        /// <summary>
        /// Registers a driver with initialization.
        /// </summary>
        public async Task RegisterAsync(string driverId, IDriver driver, DriverContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(driverId))
                throw new System.ArgumentException("Driver ID cannot be null or empty", nameof(driverId));

            if (driver == null)
                throw new System.ArgumentNullException(nameof(driver));

            if (context == null)
                throw new System.ArgumentNullException(nameof(context));

            // Ensure context has the driver ID
            if (string.IsNullOrEmpty(context.DriverId))
                context.DriverId = driverId;

            // Initialize the driver
            await driver.InitializeAsync(context, cancellationToken);

            var registration = new DriverRegistration
            {
                Driver = driver,
                Context = context,
                Type = driver.Type,
                Runtime = driver.Runtime
            };

            if (!_drivers.TryAdd(driverId, registration))
            {
                throw new DriverException($"Driver '{driverId}' is already registered", ErrorCodes.Driver.AlreadyRegistered);
            }

            // Set as default if this is the first driver
            lock (_defaultDriverLock)
            {
                if (_defaultDriverId == null)
                {
                    _defaultDriverId = driverId;
                }
            }
        }

        /// <summary>
        /// Unregisters a driver.
        /// </summary>
        public void Unregister(string driverId)
        {
            _drivers.TryRemove(driverId, out _);

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

        /// <summary>
        /// Gets the driver context.
        /// </summary>
        public DriverContext GetContext(string driverId)
        {
            if (!_drivers.TryGetValue(driverId, out var registration))
            {
                throw new DriverNotFoundException(driverId);
            }

            return registration.Context;
        }

        /// <summary>
        /// Checks if a driver is registered.
        /// </summary>
        public bool IsRegistered(string driverId)
        {
            return _drivers.ContainsKey(driverId);
        }

        /// <summary>
        /// Gets all registered driver IDs.
        /// </summary>
        public IReadOnlyList<string> GetAllDriverIds()
        {
            return _drivers.Keys.ToList();
        }

        /// <summary>
        /// Gets drivers by type.
        /// </summary>
        public IReadOnlyList<string> GetDriversByType(DriverType driverType)
        {
            return _drivers
                .Where(kvp => kvp.Value.Type == driverType)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Gets drivers by runtime.
        /// </summary>
        public IReadOnlyList<string> GetDriversByRuntime(RuntimeType runtime)
        {
            return _drivers
                .Where(kvp => kvp.Value.Runtime == runtime)
                .Select(kvp => kvp.Key)
                .ToList();
        }

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
            if (!IsRegistered(driverId))
            {
                throw new DriverNotFoundException(driverId);
            }

            lock (_defaultDriverLock)
            {
                _defaultDriverId = driverId;
            }
        }

        private class DriverRegistration
        {
            public IDriver Driver { get; set; }
            public DriverContext Context { get; set; }
            public DriverType Type { get; set; }
            public RuntimeType Runtime { get; set; }
        }
    }
}
