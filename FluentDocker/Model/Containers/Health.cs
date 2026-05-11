using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Health check status and history for a container.
  /// Populated from the "State.Health" section of container inspect.
  /// </summary>
  public class Health
  {
    /// <summary>Current health status (healthy, unhealthy, starting, none).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HealthState Status { get; set; }

    /// <summary>Number of consecutive health check failures.</summary>
    public int FailingStreak { get; set; }

    /// <summary>Recent health check execution results.</summary>
    public List<HealthLog> Log { get; set; }
  }

  /// <summary>
  /// A single health check execution result.
  /// </summary>
  public class HealthLog
  {
    /// <summary>Timestamp when the health check started (ISO 8601).</summary>
    public string Start { get; set; }

    /// <summary>Timestamp when the health check finished (ISO 8601).</summary>
    public string End { get; set; }

    /// <summary>Exit code of the health check command (0 = healthy).</summary>
    public int ExitCode { get; set; }

    /// <summary>Standard output from the health check command.</summary>
    public string Output { get; set; }
  }
}
