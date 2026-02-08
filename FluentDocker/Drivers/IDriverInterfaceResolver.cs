using System;
using System.Collections.Generic;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Optional interface for drivers and driver packs that support
    /// runtime resolution of arbitrary interface types.
    /// Implementing this allows a driver to expose custom interfaces
    /// without requiring kernel changes.
    /// </summary>
    public interface IDriverInterfaceResolver
    {
        /// <summary>
        /// Attempts to resolve an implementation for the given interface type.
        /// </summary>
        /// <param name="interfaceType">The interface type to resolve.</param>
        /// <param name="implementation">The resolved instance, or null.</param>
        /// <returns>True if the interface was resolved.</returns>
        bool TryResolve(Type interfaceType, out object implementation);

        /// <summary>
        /// Gets all interface types supported by this resolver.
        /// </summary>
        IReadOnlyCollection<Type> GetSupportedInterfaces();
    }
}
