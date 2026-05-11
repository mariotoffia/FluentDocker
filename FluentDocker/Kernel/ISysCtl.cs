using System;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// System control interface for accessing driver components.
  /// Provides methods to resolve driver interfaces by ID.
  /// </summary>
  public interface ISysCtl
  {
    /// <summary>
    /// Gets a driver component interface by driver ID and type.
    /// </summary>
    /// <typeparam name="T">Driver component interface (IContainerDriver, IImageDriver, etc.)</typeparam>
    /// <param name="driverId">Driver identifier</param>
    /// <returns>Driver component instance</returns>
    T SysCtl<T>(string driverId) where T : class;

    /// <summary>
    /// Gets a driver component interface by driver ID and runtime type.
    /// Enables resolution of arbitrary interfaces without compile-time knowledge.
    /// </summary>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="interfaceType">The interface type to resolve</param>
    /// <returns>Driver component instance</returns>
    /// <exception cref="FluentDocker.Common.DriverNotFoundException">If driver not found</exception>
    /// <exception cref="FluentDocker.Common.InterfaceNotSupportedException">If interface not supported</exception>
    object SysCtl(string driverId, Type interfaceType);

    /// <summary>
    /// Tries to get a driver component interface. Returns false instead of throwing
    /// if the interface is not supported by the driver.
    /// </summary>
    /// <typeparam name="T">Driver component interface</typeparam>
    /// <param name="driverId">Driver identifier</param>
    /// <param name="instance">The resolved instance, or null</param>
    /// <returns>True if the interface was resolved</returns>
    /// <exception cref="FluentDocker.Common.DriverNotFoundException">If driver not found</exception>
    bool TrySysCtl<T>(string driverId, out T instance) where T : class;
  }
}
