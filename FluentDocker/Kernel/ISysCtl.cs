using System;
using System.Diagnostics.CodeAnalysis;

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
    /// <param name="driverId">Driver identifier; null or whitespace resolves the default driver.</param>
    /// <returns>Driver component instance</returns>
    /// <exception cref="FluentDocker.Common.DriverNotFoundException">If driver not found</exception>
    /// <exception cref="FluentDocker.Common.InterfaceNotSupportedException">If interface not supported</exception>
    /// <exception cref="FluentDocker.Common.DriverException">If a driver or driver pack fails unexpectedly while resolving the interface</exception>
    /// <exception cref="InvalidOperationException">If no default driver is configured and <paramref name="driverId"/> is null or whitespace</exception>
    /// <exception cref="System.IO.IOException">If a driver or driver pack reports an I/O failure while resolving the interface</exception>
    /// <exception cref="ObjectDisposedException">If the kernel or driver pack has been disposed</exception>
    /// <exception cref="OperationCanceledException">If a driver or driver pack reports cancellation while resolving the interface</exception>
    T SysCtl<T>(string driverId) where T : class;

    /// <summary>
    /// Gets a driver component interface by driver ID and runtime type.
    /// Enables resolution of arbitrary interfaces without compile-time knowledge.
    /// </summary>
    /// <param name="driverId">Driver identifier; null or whitespace resolves the default driver.</param>
    /// <param name="interfaceType">The interface type to resolve</param>
    /// <returns>Driver component instance</returns>
    /// <exception cref="FluentDocker.Common.DriverNotFoundException">If driver not found</exception>
    /// <exception cref="FluentDocker.Common.InterfaceNotSupportedException">If interface not supported</exception>
    /// <exception cref="FluentDocker.Common.DriverException">If a driver or driver pack fails unexpectedly while resolving the interface</exception>
    /// <exception cref="InvalidOperationException">If no default driver is configured and <paramref name="driverId"/> is null or whitespace</exception>
    /// <exception cref="System.IO.IOException">If a driver or driver pack reports an I/O failure while resolving the interface</exception>
    /// <exception cref="ObjectDisposedException">If the kernel or driver pack has been disposed</exception>
    /// <exception cref="OperationCanceledException">If a driver or driver pack reports cancellation while resolving the interface</exception>
    object SysCtl(string driverId, Type interfaceType);

    /// <summary>
    /// Tries to get a driver component interface. Returns false instead of throwing
    /// if the interface is unsupported. Missing drivers, disposal/cancellation, I/O
    /// failures, and unexpected resolver faults still throw.
    /// </summary>
    /// <typeparam name="T">Driver component interface</typeparam>
    /// <param name="driverId">Driver identifier; null or whitespace resolves the default driver.</param>
    /// <param name="instance">The resolved instance, or null</param>
    /// <returns>True if the interface was resolved</returns>
    /// <exception cref="FluentDocker.Common.DriverNotFoundException">If driver not found</exception>
    /// <exception cref="FluentDocker.Common.DriverException">If a driver or driver pack fails unexpectedly while resolving the interface</exception>
    /// <exception cref="FluentDocker.Common.InterfaceNotSupportedException">If probing the driver pack's fallback resolver faults unexpectedly (original fault is the InnerException). A genuinely unsupported interface returns false instead of throwing.</exception>
    /// <exception cref="InvalidOperationException">If no default driver is configured and <paramref name="driverId"/> is null or whitespace</exception>
    /// <exception cref="System.IO.IOException">If a driver or driver pack reports an I/O failure while resolving the interface</exception>
    /// <exception cref="ObjectDisposedException">If the kernel or driver pack has been disposed</exception>
    /// <exception cref="OperationCanceledException">If a driver or driver pack reports cancellation while resolving the interface</exception>
    bool TrySysCtl<T>(string driverId, [NotNullWhen(true)] out T? instance) where T : class;
  }
}
