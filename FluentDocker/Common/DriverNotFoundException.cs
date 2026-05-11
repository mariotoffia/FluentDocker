using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a driver is not found in the registry.
  /// </summary>
  public class DriverNotFoundException : DriverException
  {
    /// <summary>
    /// The identifier of the driver that was not found.
    /// </summary>
    public string DriverId { get; }

    /// <summary>
    /// Initializes a new instance with the specified driver identifier.
    /// </summary>
    /// <param name="driverId">The identifier of the driver that was not found.</param>
    public DriverNotFoundException(string driverId)
        : base($"Driver '{driverId}' not found in registry", ErrorCodes.Driver.NotFound) => DriverId = driverId;

    /// <summary>
    /// Initializes a new instance with the specified driver identifier and error context.
    /// </summary>
    /// <param name="driverId">The identifier of the driver that was not found.</param>
    /// <param name="context">Diagnostic context information.</param>
    public DriverNotFoundException(string driverId, ErrorContext context)
        : base($"Driver '{driverId}' not found in registry", ErrorCodes.Driver.NotFound, context) => DriverId = driverId;
  }
}
