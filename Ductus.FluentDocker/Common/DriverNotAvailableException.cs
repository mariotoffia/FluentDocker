using System;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when a driver is not available (e.g., Docker daemon not running).
    /// </summary>
    public class DriverNotAvailableException : DriverException
    {
        public string DriverId { get; }

        public DriverNotAvailableException(string driverId, string reason)
            : base($"Driver '{driverId}' is not available: {reason}", ErrorCodes.Driver.NotAvailable, isTransient: true)
        {
            DriverId = driverId;
        }

        public DriverNotAvailableException(string driverId, string reason, ErrorContext context)
            : base($"Driver '{driverId}' is not available: {reason}", ErrorCodes.Driver.NotAvailable, context, isTransient: true)
        {
            DriverId = driverId;
        }

        public DriverNotAvailableException(string driverId, string reason, Exception innerException)
            : base($"Driver '{driverId}' is not available: {reason}", ErrorCodes.Driver.NotAvailable, innerException, isTransient: true)
        {
            DriverId = driverId;
        }
    }
}
