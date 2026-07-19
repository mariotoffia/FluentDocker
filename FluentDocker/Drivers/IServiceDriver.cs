using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Service management for orchestrated services (Docker Swarm).
  /// Supported by: Docker Swarm.
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
    /// <remarks>
    /// Docker API sends registry credentials from the current auth cache as
    /// <c>X-Registry-Auth</c> when available. Transport failures return
    /// <see cref="ErrorCodes.Api.ConnectionFailed"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled by the caller.
    /// </exception>
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
    /// <remarks>
    /// Docker API sends registry credentials from the current auth cache as
    /// <c>X-Registry-Auth</c> when a cached credential matches the image's registry.
    /// Transport failures return <see cref="ErrorCodes.Api.ConnectionFailed"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is canceled by the caller.
    /// </exception>
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
        ServiceListFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects a service.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceId">Service ID or name</param>
    /// <param name="pretty">
    /// When <c>true</c>, the CLI's human-readable rendering is returned in
    /// <see cref="ServiceDetails.Pretty"/> and the structured fields (other than
    /// <see cref="ServiceDetails.Id"/>) are NOT populated — the pretty format is not
    /// machine-parseable. Use the default <c>false</c> for structured details.
    /// </param>
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
    [JsonConverter(typeof(LenientStringListConverter))]
    public List<string> Ports { get; set; } = [];
  }

  /// <summary>
  /// Detailed service information.
  /// </summary>
  public class ServiceDetails
  {
    /// <summary>Service ID.</summary>
    public string Id { get; set; }

    /// <summary>
    /// The CLI's human-readable inspect rendering. Populated ONLY when
    /// <c>InspectAsync(..., pretty: true)</c> was requested; <c>null</c> otherwise.
    /// When set, the structured fields (except <see cref="Id"/>) are not populated.
    /// </summary>
    public string? Pretty { get; set; }

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
    /// <remarks>The value is in UTC (<see cref="DateTimeKind.Utc"/>).</remarks>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last update time.</summary>
    /// <remarks>The value is in UTC (<see cref="DateTimeKind.Utc"/>).</remarks>
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
}
