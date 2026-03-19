using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a driver is not available (e.g., Docker daemon not running).
  /// </summary>
  public class DriverNotAvailableException : DriverException
  {
    /// <summary>
    /// The identifier of the driver that is not available.
    /// </summary>
    public string DriverId { get; }

    /// <summary>
    /// Initializes a new instance with the specified driver identifier and reason.
    /// </summary>
    /// <param name="driverId">The identifier of the unavailable driver.</param>
    /// <param name="reason">The reason the driver is not available.</param>
    public DriverNotAvailableException(string driverId, string reason)
        : base($"Driver '{driverId}' is not available: {reason}", ErrorCodes.Driver.NotAvailable, null, isTransient: true) => DriverId = driverId;

    /// <summary>
    /// Initializes a new instance with the specified driver identifier, reason, and error context.
    /// </summary>
    /// <param name="driverId">The identifier of the unavailable driver.</param>
    /// <param name="reason">The reason the driver is not available.</param>
    /// <param name="context">Diagnostic context information.</param>
    public DriverNotAvailableException(string driverId, string reason, ErrorContext context)
        : base($"Driver '{driverId}' is not available: {reason}", ErrorCodes.Driver.NotAvailable, context, isTransient: true) => DriverId = driverId;

    /// <summary>
    /// Initializes a new instance with the specified driver identifier, reason, and inner exception.
    /// </summary>
    /// <param name="driverId">The identifier of the unavailable driver.</param>
    /// <param name="reason">The reason the driver is not available.</param>
    /// <param name="innerException">The exception that caused the driver to be unavailable.</param>
    public DriverNotAvailableException(string driverId, string reason, Exception innerException)
        : base($"Driver '{driverId}' is not available: {reason}", ErrorCodes.Driver.NotAvailable, null, innerException, isTransient: true) => DriverId = driverId;
  }
}
