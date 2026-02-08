using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Optional abstract base class for driver packs providing dictionary-based
    /// interface resolution. Implements IDriverInterfaceResolver and the
    /// type-based ISysCtl methods. Subclasses register drivers via RegisterDriver.
    /// </summary>
    public abstract class DriverPackBase : IDriverInterfaceResolver
    {
        /// <summary>
        /// Type-to-implementation map for registered driver interfaces.
        /// </summary>
        protected readonly Dictionary<Type, object> Drivers = new Dictionary<Type, object>();

        /// <summary>
        /// Registers a driver implementation by its interface type.
        /// </summary>
        protected void RegisterDriver<T>(T driver) where T : class
        {
            Drivers[typeof(T)] = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public bool TryResolve(Type interfaceType, out object implementation)
        {
            return Drivers.TryGetValue(interfaceType, out implementation);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<Type> GetSupportedInterfaces()
        {
            return Drivers.Keys.ToList().AsReadOnly();
        }

        /// <summary>
        /// Resolves a driver interface by type. Throws if not found.
        /// </summary>
        protected object ResolveSysCtl(string driverId, Type interfaceType)
        {
            if (Drivers.TryGetValue(interfaceType, out var driver))
                return driver;
            throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
        }

        /// <summary>
        /// Tries to resolve a driver interface by generic type.
        /// </summary>
        protected bool TryResolveSysCtl<T>(out T instance) where T : class
        {
            if (Drivers.TryGetValue(typeof(T), out var driver))
            {
                instance = (T)driver;
                return true;
            }
            instance = null;
            return false;
        }
    }
}
