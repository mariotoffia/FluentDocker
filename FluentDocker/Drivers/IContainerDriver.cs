using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Container-specific driver operations.
  /// Supported by: Docker, Podman.
  /// </summary>
  public partial interface IContainerDriver
  {
    #region Lifecycle Operations

    /// <summary>
    /// Creates a new container without starting it.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="config">Container configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container create result with ID</returns>
    Task<Model.Drivers.CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context,
        ContainerCreateConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and starts a container in one operation.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="config">Container configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Container run result with ID. For Docker/Podman CLI foreground runs
    /// (<c>Detach=false</c>), <c>Success</c> means the container was created; a non-zero
    /// container exit is returned as <see cref="ContainerRunResult.ExitCode"/> data, so
    /// callers must inspect it instead of gating on <c>Success</c> alone. The
    /// <see cref="ContainerRunResult.ExitCode"/> value carries either the container
    /// process's own exit code or the CLI's infrastructure exit codes (125 daemon/run
    /// error, 126 command not invocable, 127 command not found) — the two sources are
    /// indistinguishable in the result; callers that must tell them apart have to inspect
    /// the container. Foreground stdout
    /// and stderr are delimited with a newline when both are present. Very large foreground
    /// output is returned as a bounded tail prefixed by
    /// <see cref="FluentDocker.Common.CliOutputTruncation.Marker(int)"/>.
    /// </returns>
    Task<Model.Drivers.CommandResponse<ContainerRunResult>> RunAsync(
        DriverContext context,
        ContainerCreateConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> StartAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="timeout">Timeout in seconds before forcing stop</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> StopAsync(
        DriverContext context,
        string containerId,
        int? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="timeout">Timeout in seconds before forcing restart</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> RestartAsync(
        DriverContext context,
        string containerId,
        int? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a running container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> PauseAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unpauses a paused container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> UnpauseAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills a container by sending a signal.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="signal">Signal to send (default: SIGKILL)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> KillAsync(
        DriverContext context,
        string containerId,
        string signal = "SIGKILL",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="force">Force removal even if running</param>
    /// <param name="removeVolumes">Remove associated volumes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string containerId,
        bool force = false,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default);

    #endregion

    #region Information Operations

    /// <summary>
    /// Inspects a container to get detailed information.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed container information</returns>
    Task<Model.Drivers.CommandResponse<Container>> InspectAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists containers.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="filter">Optional filter parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// List of sparse container records. Docker CLI list results populate only
    /// <see cref="Container.Id"/>, <see cref="Container.Image"/>, <see cref="Container.Name"/>,
    /// <see cref="Container.Created"/>, and <see cref="Container.State"/> status/running fields;
    /// ports, labels, mounts, networks, and other details require <see cref="InspectAsync"/>.
    /// </returns>
    Task<Model.Drivers.CommandResponse<IList<Container>>> ListAsync(
        DriverContext context,
        ContainerListFilter? filter = null,
        CancellationToken cancellationToken = default);

    #endregion
  }

  #region Result Types

  /// <summary>
  /// Result of a container create operation.
  /// </summary>
  public class ContainerCreateResult
  {
    /// <summary>Container ID.</summary>
    public string Id { get; set; }

    /// <summary>Container name.</summary>
    public string Name { get; set; }

    /// <summary>Warnings from the create operation.</summary>
    public List<string> Warnings { get; set; } = [];
  }

  /// <summary>
  /// Result of a container run operation.
  /// </summary>
  public class ContainerRunResult
  {
    /// <summary>Container ID when the driver can determine it; Docker/Podman CLI foreground runs populate it from a cidfile.</summary>
    public string? Id { get; set; }

    /// <summary>
    /// Container output (when Detach = false) or null (when Detach = true). Docker CLI keeps
    /// only a marked tail for very large foreground output.
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// Container exit code for foreground runs where the container was created and ran to
    /// completion; null when detached or not applicable (driver-specific).
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>Warnings from the run operation.</summary>
    public List<string> Warnings { get; set; } = [];
  }

  #endregion

  #region Config Types

  /// <summary>
  /// Configuration for creating a container.
  /// </summary>
  public class ContainerCreateConfig
  {
    /// <summary>Image to use for the container.</summary>
    public string Image { get; set; }

    /// <summary>Container name.</summary>
    public string Name { get; set; }

    /// <summary>Command to run.</summary>
    public string[] Command { get; set; }

    /// <summary>Environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>Port bindings (container port -> host port).</summary>
    public Dictionary<string, string> PortBindings { get; set; } = [];

    /// <summary>Volume bindings in Docker syntax: source:target[:ro].</summary>
    public List<string> Volumes { get; set; } = [];

    /// <summary>Network mode.</summary>
    public string NetworkMode { get; set; }

    /// <summary>Additional labels.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Working directory inside the container.</summary>
    public string WorkingDirectory { get; set; }

    /// <summary>User to run as inside the container.</summary>
    public string User { get; set; }

    /// <summary>Restart policy (no, always, unless-stopped, on-failure).</summary>
    public string RestartPolicy { get; set; }

    /// <summary>Hostname of the container.</summary>
    public string Hostname { get; set; }

    /// <summary>Networks to attach the container to.</summary>
    public List<string> Networks { get; set; } = [];

    /// <summary>Static IPv4 address for the container (requires custom network with subnet).</summary>
    public string Ipv4Address { get; set; }

    /// <summary>Static IPv6 address for the container (requires IPv6-enabled network with subnet).</summary>
    public string Ipv6Address { get; set; }

    /// <summary>Memory limit in bytes.</summary>
    public long? MemoryLimit { get; set; }

    /// <summary>CPU shares (relative weight).</summary>
    public long? CpuShares { get; set; }

    /// <summary>CPU quota in microseconds per CPU period.</summary>
    public long? CpuQuota { get; set; }

    /// <summary>Whether to run in privileged mode.</summary>
    public bool Privileged { get; set; }

    /// <summary>Whether to auto-remove container when it exits.</summary>
    public bool AutoRemove { get; set; }

    /// <summary>Whether to run in detached mode (for Run operation).</summary>
    public bool Detach { get; set; } = true;

    /// <summary>Whether to allocate a TTY.</summary>
    public bool Tty { get; set; }

    /// <summary>Whether to keep STDIN open; foreground Docker CLI runs close it unless input is supplied internally.</summary>
    public bool Interactive { get; set; }

    /// <summary>Entrypoint override.</summary>
    public string[] Entrypoint { get; set; }

    /// <summary>Stop signal.</summary>
    public string StopSignal { get; set; }

    /// <summary>Stop timeout in seconds.</summary>
    public int? StopTimeout { get; set; }

    /// <summary>Health check configuration.</summary>
    public HealthCheckConfig HealthCheck { get; set; }

    /// <summary>DNS servers.</summary>
    public List<string> Dns { get; set; } = [];

    /// <summary>Extra hosts (/etc/hosts entries).</summary>
    public Dictionary<string, string> ExtraHosts { get; set; } = [];

    /// <summary>
    /// Container links (legacy Docker feature).
    /// Format: "container:alias" or just "container" (alias defaults to container name).
    /// </summary>
    /// <remarks>
    /// Container linking is a legacy Docker feature. User-defined networks are the preferred approach.
    /// Links allow containers to discover each other and securely transfer information.
    /// </remarks>
    public List<string> Links { get; set; } = [];

    /// <summary>Podman pod to join (Podman-only, ignored by Docker).</summary>
    public string Pod { get; set; }

    /// <summary>
    /// Network aliases keyed by network name.
    /// Each entry maps a network name to one or more aliases for the container on that network.
    /// </summary>
    public Dictionary<string, List<string>> NetworkAliases { get; set; } = [];

    /// <summary>Linux capabilities to add (e.g. SYS_PTRACE, NET_ADMIN).</summary>
    public List<string> CapAdd { get; set; } = [];

    /// <summary>Linux capabilities to drop (e.g. NET_RAW, MKNOD).</summary>
    public List<string> CapDrop { get; set; } = [];

    /// <summary>Security options (e.g. seccomp=unconfined, apparmor=docker-default).</summary>
    public List<string> SecurityOpt { get; set; } = [];

    /// <summary>Size of /dev/shm in bytes.</summary>
    public long? ShmSize { get; set; }

    /// <summary>Tmpfs mounts. Key = container path, Value = options (e.g. "rw,noexec,size=64m").</summary>
    public Dictionary<string, string> Tmpfs { get; set; } = [];

    /// <summary>Device mappings. Key = host device, Value = container device path.</summary>
    public Dictionary<string, string> Devices { get; set; } = [];

    /// <summary>Whether the root filesystem is read-only.</summary>
    public bool ReadonlyRootfs { get; set; }

    /// <summary>Platform for multi-arch images (e.g. linux/arm64).</summary>
    public string Platform { get; set; }

    /// <summary>OCI runtime to use (e.g. runc, crun, runsc).</summary>
    public string Runtime { get; set; }
  }

  /// <summary>
  /// Configuration for container health check.
  /// </summary>
  public class HealthCheckConfig
  {
    /// <summary>
    /// Command to run for health check. Docker CLI exec-form commands assume a Linux shell;
    /// they are not translated for Windows containers. Docker/Podman CLI exec-form
    /// <c>CMD</c> values are serialized into the string-only health command flag, so
    /// shell-less images cannot run them without providing a shell-compatible command.
    /// </summary>
    public string[] Test { get; set; }

    /// <summary>Interval between health checks.</summary>
    public string Interval { get; set; }

    /// <summary>Timeout for health check.</summary>
    public string Timeout { get; set; }

    /// <summary>Number of retries.</summary>
    public int Retries { get; set; }

    /// <summary>Start period before health checks begin.</summary>
    public string StartPeriod { get; set; }
  }

  /// <summary>
  /// Filter parameters for listing containers.
  /// </summary>
  public class ContainerListFilter
  {
    /// <summary>Include all containers (default: only running).</summary>
    public bool All { get; set; }

    /// <summary>Filter by status.</summary>
    public string Status { get; set; }

    /// <summary>Filter by name.</summary>
    public string Name { get; set; }

    /// <summary>Filter by ID.</summary>
    public string Id { get; set; }

    /// <summary>Filter by ancestor image.</summary>
    public string Ancestor { get; set; }

    /// <summary>Filter by label.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Limit number of results.</summary>
    public int? Limit { get; set; }
  }

  #endregion
}
