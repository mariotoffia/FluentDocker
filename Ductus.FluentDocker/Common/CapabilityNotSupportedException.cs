using System;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when a driver does not support a requested capability.
    /// </summary>
    public class CapabilityNotSupportedException : DriverException
    {
        public string DriverId { get; }
        public string CapabilityName { get; }

        public CapabilityNotSupportedException(string driverId, string capabilityName)
            : base($"Driver '{driverId}' does not support capability '{capabilityName}'", ErrorCodes.Driver.CapabilityNotSupported)
        {
            DriverId = driverId;
            CapabilityName = capabilityName;
        }

        public CapabilityNotSupportedException(string driverId, string capabilityName, ErrorContext context)
            : base($"Driver '{driverId}' does not support capability '{capabilityName}'", ErrorCodes.Driver.CapabilityNotSupported, context)
        {
            DriverId = driverId;
            CapabilityName = capabilityName;
        }
    }
}
