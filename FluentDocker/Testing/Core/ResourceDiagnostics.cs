using System;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Diagnostic information collected when a resource fails to initialize.
  /// </summary>
  public class ResourceDiagnostics
  {
    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    public Exception Failure { get; set; }

    /// <summary>
    /// Resource name at the time of failure.
    /// </summary>
    public string ResourceName { get; set; }

    /// <summary>
    /// Driver ID used.
    /// </summary>
    public string DriverId { get; set; }

    /// <summary>
    /// Container/service inspect payload (JSON), if available.
    /// </summary>
    public string InspectPayload { get; set; }

    /// <summary>
    /// Logs collected from the resource, if available.
    /// </summary>
    public string Logs { get; set; }

    /// <summary>
    /// Additional context about the operation.
    /// </summary>
    public string OperationContext { get; set; }
  }

  /// <summary>
  /// Diagnostics captured when teardown fails during disposal.
  /// </summary>
  public class TeardownDiagnostics
  {
    /// <summary>
    /// The exception from the graceful teardown attempt.
    /// </summary>
    public Exception? TeardownException { get; init; }

    /// <summary>
    /// The exception from the force-remove attempt, or null if it succeeded.
    /// </summary>
    public Exception? ForceRemoveException { get; init; }
  }
}
