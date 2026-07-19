#nullable enable
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
    /// Initializes a new instance with the specified driver, interface, and root cause.
    /// </summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="interfaceName">The name of the unsupported interface.</param>
    /// <param name="innerException">The exception that caused resolution to fail.</param>
    public InterfaceNotSupportedException(string driverId, string interfaceName, Exception innerException)
        : base(
            $"Driver '{driverId}' does not implement interface '{interfaceName}'",
            ErrorCodes.Driver.InterfaceNotSupported,
            innerException)
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

    /// <summary>
    /// Initializes a new instance for a resolved instance that is not assignable to the requested
    /// interface — a mis-mapped driver/pack registration (for example
    /// <c>Drivers[typeof(IContainerDriver)] = imageDriver</c>) rather than a genuinely unimplemented
    /// interface.
    /// </summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="interfaceName">The name of the requested interface.</param>
    /// <param name="actualType">The type that was actually resolved.</param>
    public InterfaceNotSupportedException(string driverId, string interfaceName, Type actualType)
        : base(
            $"Driver '{driverId}' resolved '{TypeNameFormatter.Format(actualType)}' but it is not assignable to interface '{interfaceName}'",
            ErrorCodes.Driver.InterfaceNotSupported)
    {
      DriverId = driverId;
      InterfaceName = interfaceName;
    }
  }
}
