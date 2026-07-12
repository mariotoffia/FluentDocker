#nullable enable
namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Container health-check status, mirrored from Docker/Podman inspect <c>State.Health.Status</c>.
  /// </summary>
  public enum HealthState
  {
    /// <summary>The status could not be determined (missing, unparsable, or a structured-token drift).</summary>
    Unknown,

    /// <summary>The health check's start period is in progress; results are not yet reliable.</summary>
    Starting,

    /// <summary>The most recent health check(s) failed.</summary>
    Unhealthy,

    /// <summary>The most recent health check succeeded.</summary>
    Healthy,

    /// <summary>No health check is configured for the container.</summary>
    None
  }
}
