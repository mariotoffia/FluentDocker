using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a driver does not implement a requested interface.
  /// </summary>
  public class InterfaceNotSupportedException : DriverException
  {
    public string DriverId { get; }
    public string InterfaceName { get; }

    public InterfaceNotSupportedException(string driverId, string interfaceName)
        : base($"Driver '{driverId}' does not implement interface '{interfaceName}'", ErrorCodes.Driver.InterfaceNotSupported)
    {
      DriverId = driverId;
      InterfaceName = interfaceName;
    }

    public InterfaceNotSupportedException(string driverId, string interfaceName, ErrorContext context)
        : base($"Driver '{driverId}' does not implement interface '{interfaceName}'", ErrorCodes.Driver.InterfaceNotSupported, context)
    {
      DriverId = driverId;
      InterfaceName = interfaceName;
    }
  }
}
