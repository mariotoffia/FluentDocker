#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        : base(BuildMessage(driverId, null), ErrorCodes.Driver.NotFound) => DriverId = driverId;

    /// <summary>
    /// Initializes a new instance with the specified driver identifier and known registered driver IDs.
    /// </summary>
    /// <param name="driverId">The identifier of the driver that was not found.</param>
    /// <param name="registeredDriverIds">Currently registered driver identifiers.</param>
    public DriverNotFoundException(string driverId, IEnumerable<string>? registeredDriverIds)
        : base(BuildMessage(driverId, registeredDriverIds), ErrorCodes.Driver.NotFound) => DriverId = driverId;

    /// <summary>
    /// Initializes a new instance with the specified driver identifier and error context.
    /// </summary>
    /// <param name="driverId">The identifier of the driver that was not found.</param>
    /// <param name="context">Diagnostic context information.</param>
    public DriverNotFoundException(string driverId, ErrorContext context)
        : base(BuildMessage(driverId, null), ErrorCodes.Driver.NotFound, context) => DriverId = driverId;

    /// <summary>
    /// Initializes a new instance with known registered driver IDs and error context.
    /// </summary>
    /// <param name="driverId">The identifier of the driver that was not found.</param>
    /// <param name="registeredDriverIds">Currently registered driver identifiers.</param>
    /// <param name="context">Diagnostic context information.</param>
    public DriverNotFoundException(
        string driverId,
        IEnumerable<string>? registeredDriverIds,
        ErrorContext context)
        : base(BuildMessage(driverId, registeredDriverIds), ErrorCodes.Driver.NotFound, context) => DriverId = driverId;

    private static string BuildMessage(string driverId, IEnumerable<string>? registeredDriverIds)
    {
      var registered = registeredDriverIds?.ToArray();
      var message = $"Driver '{driverId}' not found in registry";
      return registered == null
          ? message
          : $"{message}; registered: [{string.Join(", ", registered)}]";
    }
  }
}
