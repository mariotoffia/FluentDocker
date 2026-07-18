using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

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
    /// <remarks>
    /// The returned instance is a borrowed view owned by the registered driver or driver pack.
    /// Resolve it fresh per operation; do not cache it across unregister or kernel disposal.
    /// Unregistering the driver disposes the owner, so held ports may later fault (for example,
    /// <see cref="ObjectDisposedException"/>), and fresh resolution throws
    /// <see cref="FluentDocker.Common.DriverNotFoundException"/>. After kernel disposal,
    /// fresh resolution throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
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
    /// <remarks>
    /// The returned instance is a borrowed view owned by the registered driver or driver pack.
    /// Resolve it fresh per operation; do not cache it across unregister or kernel disposal.
    /// Unregistering the driver disposes the owner, so held ports may later fault (for example,
    /// <see cref="ObjectDisposedException"/>), and fresh resolution throws
    /// <see cref="FluentDocker.Common.DriverNotFoundException"/>. After kernel disposal,
    /// fresh resolution throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
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
    /// <exception cref="InvalidOperationException">If no default driver is configured and <paramref name="driverId"/> is null or whitespace</exception>
    /// <exception cref="System.IO.IOException">If a driver or driver pack reports an I/O failure while resolving the interface</exception>
    /// <exception cref="ObjectDisposedException">If the kernel or driver pack has been disposed</exception>
    /// <exception cref="OperationCanceledException">If a driver or driver pack reports cancellation while resolving the interface</exception>
    /// <remarks>
    /// The returned instance is a borrowed view owned by the registered driver or driver pack.
    /// Resolve it fresh per operation; do not cache it across unregister or kernel disposal.
    /// Unregistering the driver disposes the owner, so held ports may later fault (for example,
    /// <see cref="ObjectDisposedException"/>), and fresh resolution throws
    /// <see cref="FluentDocker.Common.DriverNotFoundException"/>. After kernel disposal,
    /// fresh resolution throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
    bool TrySysCtl<T>(string driverId, [NotNullWhen(true)] out T? instance) where T : class;

    /// <summary>
    /// The declared capability surface of a driver/pack. Exposed on the abstraction so capability
    /// gating works through <see cref="ISysCtl"/> (e.g. a <c>BuildScope.Kernel</c>) without a
    /// downcast to the concrete kernel (KRN-MAJ-6). This reflects the driver's declared surface,
    /// not live daemon feature availability.
    /// </summary>
    /// <param name="driverId">Driver identifier; null/whitespace resolves the default driver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">If no default driver is configured and <paramref name="driverId"/> is null or whitespace</exception>
    Task<Model.Drivers.DriverCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken cancellationToken = default);

    /// <summary>Whether the resolved driver/pack reports healthy.</summary>
    /// <param name="driverId">Driver identifier; null/whitespace resolves the default driver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">If no default driver is configured and <paramref name="driverId"/> is null or whitespace</exception>
    Task<bool> IsHealthyAsync(string driverId, CancellationToken cancellationToken = default);

    /// <summary>The configured default driver id, or <c>null</c> when none is set.</summary>
    string? DefaultDriverId { get; }
  }
}
