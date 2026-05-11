using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a driver does not support a requested capability.
  /// </summary>
  public class CapabilityNotSupportedException : DriverException
  {
    /// <summary>
    /// The identifier of the driver that does not support the capability.
    /// </summary>
    public string DriverId { get; }

    /// <summary>
    /// The name of the unsupported capability.
    /// </summary>
    public string CapabilityName { get; }

    /// <summary>
    /// Initializes a new instance with the specified driver and capability identifiers.
    /// </summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="capabilityName">The name of the unsupported capability.</param>
    public CapabilityNotSupportedException(string driverId, string capabilityName)
        : base($"Driver '{driverId}' does not support capability '{capabilityName}'", ErrorCodes.Driver.CapabilityNotSupported)
    {
      DriverId = driverId;
      CapabilityName = capabilityName;
    }

    /// <summary>
    /// Initializes a new instance with the specified driver, capability, and error context.
    /// </summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="capabilityName">The name of the unsupported capability.</param>
    /// <param name="context">Diagnostic context information.</param>
    public CapabilityNotSupportedException(string driverId, string capabilityName, ErrorContext context)
        : base($"Driver '{driverId}' does not support capability '{capabilityName}'", ErrorCodes.Driver.CapabilityNotSupported, context)
    {
      DriverId = driverId;
      CapabilityName = capabilityName;
    }
  }
}
