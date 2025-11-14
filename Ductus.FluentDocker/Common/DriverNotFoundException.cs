using System;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when a driver is not found in the registry.
    /// </summary>
    public class DriverNotFoundException : DriverException
    {
        public string DriverId { get; }

        public DriverNotFoundException(string driverId)
            : base($"Driver '{driverId}' not found in registry", ErrorCodes.Driver.NotFound)
        {
            DriverId = driverId;
        }

        public DriverNotFoundException(string driverId, ErrorContext context)
            : base($"Driver '{driverId}' not found in registry", ErrorCodes.Driver.NotFound, context)
        {
            DriverId = driverId;
        }
    }
}
