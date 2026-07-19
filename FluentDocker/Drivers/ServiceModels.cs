using System.Collections.Generic;

namespace FluentDocker.Drivers
{
  #region Config Types

  /// <summary>
  /// Configuration for creating a service.
  /// </summary>
  public class ServiceCreateConfig
  {
    /// <summary>Service name.</summary>
    public string Name { get; set; }

    /// <summary>Image to use.</summary>
    public string Image { get; set; }

    /// <summary>Command to run.</summary>
    public string[] Command { get; set; }

    /// <summary>Arguments to command.</summary>
    public string[] Args { get; set; }

    /// <summary>Number of replicas.</summary>
    public int? Replicas { get; set; }

    /// <summary>Service mode (replicated, global).</summary>
    public string Mode { get; set; }

    /// <summary>Environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>Labels.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Container labels.</summary>
    public Dictionary<string, string> ContainerLabels { get; set; } = [];

    /// <summary>Published ports.</summary>
    public List<ServicePort> Ports { get; set; } = [];

    /// <summary>Networks to attach.</summary>
    public List<string> Networks { get; set; } = [];

    /// <summary>Mounts.</summary>
    public List<ServiceMount> Mounts { get; set; } = [];

    /// <summary>Working directory.</summary>
    public string WorkDir { get; set; }

    /// <summary>User to run as.</summary>
    public string User { get; set; }

    /// <summary>Placement constraints.</summary>
    public List<string> Constraints { get; set; } = [];

    /// <summary>Resource limits.</summary>
    public ServiceResources Limits { get; set; }

    /// <summary>Resource reservations.</summary>
    public ServiceResources Reservations { get; set; }

    /// <summary>Update configuration.</summary>
    public ServiceUpdateSettings UpdateConfig { get; set; }

    /// <summary>Rollback configuration.</summary>
    public ServiceUpdateSettings RollbackConfig { get; set; }

    /// <summary>Restart condition (none, on-failure, any).</summary>
    public string RestartCondition { get; set; }

    /// <summary>Restart delay.</summary>
    public string RestartDelay { get; set; }

    /// <summary>Maximum restart attempts.</summary>
    public int? RestartMaxAttempts { get; set; }

    /// <summary>Restart window.</summary>
    public string RestartWindow { get; set; }

    /// <summary>Health check command.</summary>
    public string HealthCmd { get; set; }

    /// <summary>Health check interval.</summary>
    public string HealthInterval { get; set; }

    /// <summary>Health check timeout.</summary>
    public string HealthTimeout { get; set; }

    /// <summary>Health check retries.</summary>
    public int? HealthRetries { get; set; }

    /// <summary>Health check start period.</summary>
    public string HealthStartPeriod { get; set; }

    /// <summary>Secrets to expose.</summary>
    public List<string> Secrets { get; set; } = [];

    /// <summary>Configs to expose.</summary>
    public List<string> Configs { get; set; } = [];

    /// <summary>Log driver.</summary>
    public string LogDriver { get; set; }

    /// <summary>Log driver options.</summary>
    public Dictionary<string, string> LogOpts { get; set; } = [];

    /// <summary>Endpoint mode (vip, dnsrr).</summary>
    public string EndpointMode { get; set; }

    /// <summary>Stop grace period.</summary>
    public string StopGracePeriod { get; set; }

    /// <summary>Detach immediately.</summary>
    public bool Detach { get; set; }

    /// <summary>Quiet mode.</summary>
    public bool Quiet { get; set; }
  }

  /// <summary>
  /// Configuration for updating a service.
  /// </summary>
  public class ServiceUpdateConfig
  {
    /// <summary>New image.</summary>
    public string Image { get; set; }

    /// <summary>Environment variables to add.</summary>
    public Dictionary<string, string> EnvAdd { get; set; } = [];

    /// <summary>Environment variables to remove.</summary>
    public List<string> EnvRm { get; set; } = [];

    /// <summary>Labels to add.</summary>
    public Dictionary<string, string> LabelAdd { get; set; } = [];

    /// <summary>Labels to remove.</summary>
    public List<string> LabelRm { get; set; } = [];

    /// <summary>Mounts to add.</summary>
    public List<ServiceMount> MountAdd { get; set; } = [];

    /// <summary>Mounts to remove.</summary>
    public List<string> MountRm { get; set; } = [];

    /// <summary>Ports to add.</summary>
    public List<ServicePort> PublishAdd { get; set; } = [];

    /// <summary>Ports to remove.</summary>
    public List<int> PublishRm { get; set; } = [];

    /// <summary>Constraints to add.</summary>
    public List<string> ConstraintAdd { get; set; } = [];

    /// <summary>Constraints to remove.</summary>
    public List<string> ConstraintRm { get; set; } = [];

    /// <summary>Networks to add.</summary>
    public List<string> NetworkAdd { get; set; } = [];

    /// <summary>Networks to remove.</summary>
    public List<string> NetworkRm { get; set; } = [];

    /// <summary>Number of replicas.</summary>
    public int? Replicas { get; set; }

    /// <summary>Resource limits.</summary>
    public ServiceResources Limits { get; set; }

    /// <summary>Resource reservations.</summary>
    public ServiceResources Reservations { get; set; }

    /// <summary>Force update even if no changes.</summary>
    public bool Force { get; set; }

    /// <summary>Rollback to previous specification.</summary>
    public bool Rollback { get; set; }

    /// <summary>Detach immediately.</summary>
    public bool Detach { get; set; }

    /// <summary>Quiet mode.</summary>
    public bool Quiet { get; set; }
  }

  /// <summary>
  /// Filter for listing services.
  /// </summary>
  public class ServiceListFilter
  {
    /// <summary>Filter by service ID.</summary>
    public string Id { get; set; }

    /// <summary>Filter by service name.</summary>
    public string Name { get; set; }

    /// <summary>Filter by label.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Filter by mode.</summary>
    public string Mode { get; set; }

    /// <summary>Ignored by built-in adapters; output format is fixed to JSON for parsing.</summary>
    public string Format { get; set; }

    /// <summary>Only display service IDs.</summary>
    public bool Quiet { get; set; }
  }

  #endregion

  #region Result Types

  /// <summary>
  /// Result of a service create operation.
  /// </summary>
  public class ServiceCreateResult
  {
    /// <summary>Service ID.</summary>
    public string Id { get; set; }

    /// <summary>Warnings from the operation.</summary>
    public List<string> Warnings { get; set; } = [];
  }

  #endregion
}
