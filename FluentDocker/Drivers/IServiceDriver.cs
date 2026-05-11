using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Service management for orchestrated services (Docker Swarm, Kubernetes).
  /// Supported by: Docker Swarm, Kubernetes (partial)
  /// Not supported by: Podman (use pods instead)
  /// </summary>
  public partial interface IServiceDriver
  {
    #region Lifecycle Operations

    /// <summary>
    /// Creates a new service.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="config">Service configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service create result</returns>
    Task<CommandResponse<ServiceCreateResult>> CreateAsync(
        DriverContext context,
        ServiceCreateConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes services.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceIds">Service IDs or names</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string[] serviceIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a service.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceId">Service ID or name</param>
    /// <param name="config">Update configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context,
        string serviceId,
        ServiceUpdateConfig config,
        CancellationToken cancellationToken = default);

    #endregion

    #region Information Operations

    /// <summary>
    /// Lists services.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="filter">Optional filter parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of services</returns>
    Task<CommandResponse<IList<ServiceInfo>>> ListAsync(
        DriverContext context,
        ServiceListFilter filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects a service.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceId">Service ID or name</param>
    /// <param name="pretty">Format output for readability</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service details</returns>
    Task<CommandResponse<ServiceDetails>> InspectAsync(
        DriverContext context,
        string serviceId,
        bool pretty = false,
        CancellationToken cancellationToken = default);

    #endregion
  }

  #region Info Types

  /// <summary>
  /// Represents a service.
  /// </summary>
  public class ServiceInfo
  {
    /// <summary>Service ID.</summary>
    public string Id { get; set; }

    /// <summary>Service name.</summary>
    public string Name { get; set; }

    /// <summary>Service mode (replicated, global).</summary>
    public string Mode { get; set; }

    /// <summary>Replicas status (e.g., "3/3").</summary>
    public string Replicas { get; set; }

    /// <summary>Image used.</summary>
    public string Image { get; set; }

    /// <summary>Ports exposed.</summary>
    public List<string> Ports { get; set; } = [];
  }

  /// <summary>
  /// Detailed service information.
  /// </summary>
  public class ServiceDetails
  {
    /// <summary>Service ID.</summary>
    public string Id { get; set; }

    /// <summary>Service version.</summary>
    public long Version { get; set; }

    /// <summary>Service name.</summary>
    public string Name { get; set; }

    /// <summary>Service mode (replicated, global).</summary>
    public string Mode { get; set; }

    /// <summary>Number of replicas.</summary>
    public int Replicas { get; set; }

    /// <summary>Image used.</summary>
    public string Image { get; set; }

    /// <summary>Command.</summary>
    public string[] Command { get; set; }

    /// <summary>Arguments.</summary>
    public string[] Args { get; set; }

    /// <summary>Environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>Labels.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Published ports.</summary>
    public List<ServicePort> Ports { get; set; } = [];

    /// <summary>Networks attached.</summary>
    public List<string> Networks { get; set; } = [];

    /// <summary>Mounts.</summary>
    public List<ServiceMount> Mounts { get; set; } = [];

    /// <summary>Update configuration.</summary>
    public ServiceUpdateSettings UpdateConfig { get; set; }

    /// <summary>Rollback configuration.</summary>
    public ServiceUpdateSettings RollbackConfig { get; set; }

    /// <summary>Resource limits.</summary>
    public ServiceResources Limits { get; set; }

    /// <summary>Resource reservations.</summary>
    public ServiceResources Reservations { get; set; }

    /// <summary>Placement constraints.</summary>
    public List<string> Constraints { get; set; } = [];

    /// <summary>Creation time.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last update time.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Raw JSON.</summary>
    public string RawJson { get; set; }
  }

  /// <summary>
  /// Represents a service port mapping.
  /// </summary>
  public class ServicePort
  {
    /// <summary>Published port on host.</summary>
    public int PublishedPort { get; set; }

    /// <summary>Target port in container.</summary>
    public int TargetPort { get; set; }

    /// <summary>Protocol (tcp, udp).</summary>
    public string Protocol { get; set; } = "tcp";

    /// <summary>Publish mode (ingress, host).</summary>
    public string PublishMode { get; set; } = "ingress";
  }

  /// <summary>
  /// Represents a service mount.
  /// </summary>
  public class ServiceMount
  {
    /// <summary>Mount type (bind, volume, tmpfs).</summary>
    public string Type { get; set; }

    /// <summary>Source path or volume name.</summary>
    public string Source { get; set; }

    /// <summary>Target path in container.</summary>
    public string Target { get; set; }

    /// <summary>Read-only flag.</summary>
    public bool ReadOnly { get; set; }
  }

  /// <summary>
  /// Service update/rollback settings.
  /// </summary>
  public class ServiceUpdateSettings
  {
    /// <summary>Parallelism.</summary>
    public int Parallelism { get; set; }

    /// <summary>Delay between updates.</summary>
    public string Delay { get; set; }

    /// <summary>Failure action (pause, continue, rollback).</summary>
    public string FailureAction { get; set; }

    /// <summary>Monitor period after update.</summary>
    public string Monitor { get; set; }

    /// <summary>Maximum failure ratio.</summary>
    public double MaxFailureRatio { get; set; }

    /// <summary>Order (stop-first, start-first).</summary>
    public string Order { get; set; }
  }

  /// <summary>
  /// Service resource configuration.
  /// </summary>
  public class ServiceResources
  {
    /// <summary>CPU limit/reservation (e.g., "0.5").</summary>
    public string Cpu { get; set; }

    /// <summary>Memory limit/reservation (e.g., "512M").</summary>
    public string Memory { get; set; }
  }

  #endregion

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

    /// <summary>Output format.</summary>
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
