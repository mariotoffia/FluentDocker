using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a driver does not implement a requested interface.
  /// </summary>
  public class InterfaceNotSupportedException : DriverException
  {
    /// <summary>
    /// The identifier of the driver that does not implement the interface.
    /// </summary>
    public string DriverId { get; }

    /// <summary>
    /// The name of the interface that is not supported.
    /// </summary>
    public string InterfaceName { get; }

    /// <summary>
    /// Initializes a new instance with the specified driver and interface identifiers.
    /// </summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="interfaceName">The name of the unsupported interface.</param>
    public InterfaceNotSupportedException(string driverId, string interfaceName)
        : base($"Driver '{driverId}' does not implement interface '{interfaceName}'", ErrorCodes.Driver.InterfaceNotSupported)
    {
      DriverId = driverId;
      InterfaceName = interfaceName;
    }

    /// <summary>
    /// Initializes a new instance with the specified driver, interface, and error context.
    /// </summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="interfaceName">The name of the unsupported interface.</param>
    /// <param name="context">Diagnostic context information.</param>
    public InterfaceNotSupportedException(string driverId, string interfaceName, ErrorContext context)
        : base($"Driver '{driverId}' does not implement interface '{interfaceName}'", ErrorCodes.Driver.InterfaceNotSupported, context)
    {
      DriverId = driverId;
      InterfaceName = interfaceName;
    }
  }
}
