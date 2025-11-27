using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using DriverCommandResponse = FluentDocker.Model.Drivers.CommandResponse<FluentDocker.Model.Drivers.Unit>;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Container-specific driver operations.
    /// Supported by: Docker, Podman, Kubernetes (partial - pods)
    /// </summary>
    public interface IContainerDriver
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
        /// <returns>Container run result with ID</returns>
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

        /// <summary>
        /// Waits for a container to stop and returns exit code.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container wait result with exit code</returns>
        Task<Model.Drivers.CommandResponse<ContainerWaitResult>> WaitAsync(
            DriverContext context,
            string containerId,
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
        /// <returns>List of containers</returns>
        Task<Model.Drivers.CommandResponse<IList<Container>>> ListAsync(
            DriverContext context,
            ContainerListFilter filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets logs from a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="follow">Follow log output</param>
        /// <param name="tail">Number of lines to show from end (null = all)</param>
        /// <param name="timestamps">Show timestamps</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container logs</returns>
        Task<Model.Drivers.CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            string containerId,
            bool follow = false,
            int? tail = null,
            bool timestamps = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets running processes in a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="psOptions">Optional ps command options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container processes</returns>
        Task<Model.Drivers.CommandResponse<ContainerProcesses>> TopAsync(
            DriverContext context,
            string containerId,
            string psOptions = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Shows changes to container's filesystem.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of filesystem changes</returns>
        Task<Model.Drivers.CommandResponse<IList<FilesystemChange>>> DiffAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default);

        #endregion

        #region Execution Operations

        /// <summary>
        /// Executes a command in a running container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="config">Exec configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Exec result</returns>
        Task<Model.Drivers.CommandResponse<ExecResult>> ExecAsync(
            DriverContext context,
            string containerId,
            ExecConfig config,
            CancellationToken cancellationToken = default);

        #endregion

        #region Copy Operations

        /// <summary>
        /// Copies files to a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="hostPath">Path on the host</param>
        /// <param name="containerPath">Path in the container</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Model.Drivers.CommandResponse<Unit>> CopyToAsync(
            DriverContext context,
            string containerId,
            string hostPath,
            string containerPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies files from a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="containerPath">Path in the container</param>
        /// <param name="hostPath">Path on the host</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Model.Drivers.CommandResponse<Unit>> CopyFromAsync(
            DriverContext context,
            string containerId,
            string containerPath,
            string hostPath,
            CancellationToken cancellationToken = default);

        #endregion

        #region Export/Update Operations

        /// <summary>
        /// Exports a container to a tar archive.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="outputPath">Output file path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Model.Drivers.CommandResponse<Unit>> ExportAsync(
            DriverContext context,
            string containerId,
            string outputPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="newName">New container name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Model.Drivers.CommandResponse<Unit>> RenameAsync(
            DriverContext context,
            string containerId,
            string newName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates container resource limits.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="config">Update configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Model.Drivers.CommandResponse<Unit>> UpdateAsync(
            DriverContext context,
            string containerId,
            ContainerUpdateConfig config,
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

        /// <summary>Warnings from the create operation.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of a container run operation.
    /// </summary>
    public class ContainerRunResult
    {
        /// <summary>Container ID.</summary>
        public string Id { get; set; }

        /// <summary>Warnings from the run operation.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of a container wait operation.
    /// </summary>
    public class ContainerWaitResult
    {
        /// <summary>Exit code from the container.</summary>
        public int ExitCode { get; set; }

        /// <summary>Error message if any.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Result of an exec operation.
    /// </summary>
    public class ExecResult
    {
        /// <summary>Exit code from the command.</summary>
        public int ExitCode { get; set; }

        /// <summary>Standard output from the command.</summary>
        public string StdOut { get; set; }

        /// <summary>Standard error from the command.</summary>
        public string StdErr { get; set; }
    }

    /// <summary>
    /// Represents a container process.
    /// </summary>
    public class ContainerProcesses
    {
        /// <summary>Column titles.</summary>
        public List<string> Titles { get; set; } = new List<string>();

        /// <summary>Process rows.</summary>
        public List<List<string>> Processes { get; set; } = new List<List<string>>();
    }

    /// <summary>
    /// Represents a filesystem change in a container.
    /// </summary>
    public class FilesystemChange
    {
        /// <summary>Path that changed.</summary>
        public string Path { get; set; }

        /// <summary>Type of change (A=Added, C=Changed, D=Deleted).</summary>
        public string Kind { get; set; }
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
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>Port bindings (container port -> host port).</summary>
        public Dictionary<string, string> PortBindings { get; set; } = new Dictionary<string, string>();

        /// <summary>Volume bindings (host path -> container path or volume name).</summary>
        public Dictionary<string, string> Volumes { get; set; } = new Dictionary<string, string>();

        /// <summary>Network mode.</summary>
        public string NetworkMode { get; set; }

        /// <summary>Additional labels.</summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        /// <summary>Working directory inside the container.</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>User to run as inside the container.</summary>
        public string User { get; set; }

        /// <summary>Restart policy (no, always, unless-stopped, on-failure).</summary>
        public string RestartPolicy { get; set; }

        /// <summary>Hostname of the container.</summary>
        public string Hostname { get; set; }

        /// <summary>Networks to attach the container to.</summary>
        public List<string> Networks { get; set; } = new List<string>();

        /// <summary>Memory limit in bytes.</summary>
        public long? MemoryLimit { get; set; }

        /// <summary>CPU shares (relative weight).</summary>
        public long? CpuShares { get; set; }

        /// <summary>Whether to run in privileged mode.</summary>
        public bool Privileged { get; set; }

        /// <summary>Whether to auto-remove container when it exits.</summary>
        public bool AutoRemove { get; set; }

        /// <summary>Whether to run in detached mode (for Run operation).</summary>
        public bool Detach { get; set; } = true;

        /// <summary>Whether to allocate a TTY.</summary>
        public bool Tty { get; set; }

        /// <summary>Whether to keep STDIN open.</summary>
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
        public List<string> Dns { get; set; } = new List<string>();

        /// <summary>Extra hosts (/etc/hosts entries).</summary>
        public Dictionary<string, string> ExtraHosts { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Configuration for container health check.
    /// </summary>
    public class HealthCheckConfig
    {
        /// <summary>Command to run for health check.</summary>
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
    /// Configuration for exec operation.
    /// </summary>
    public class ExecConfig
    {
        /// <summary>Command to execute.</summary>
        public string[] Command { get; set; }

        /// <summary>Working directory inside the container.</summary>
        public string WorkingDir { get; set; }

        /// <summary>Environment variables.</summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>User to run as.</summary>
        public string User { get; set; }

        /// <summary>Whether to run in privileged mode.</summary>
        public bool Privileged { get; set; }

        /// <summary>Allocate a TTY.</summary>
        public bool Tty { get; set; }

        /// <summary>Keep STDIN attached.</summary>
        public bool Interactive { get; set; }

        /// <summary>Detach from command after starting.</summary>
        public bool Detach { get; set; }
    }

    /// <summary>
    /// Configuration for updating container resources.
    /// </summary>
    public class ContainerUpdateConfig
    {
        /// <summary>Memory limit in bytes.</summary>
        public long? MemoryLimit { get; set; }

        /// <summary>Memory reservation in bytes.</summary>
        public long? MemoryReservation { get; set; }

        /// <summary>CPU shares (relative weight).</summary>
        public long? CpuShares { get; set; }

        /// <summary>CPU period in microseconds.</summary>
        public long? CpuPeriod { get; set; }

        /// <summary>CPU quota in microseconds.</summary>
        public long? CpuQuota { get; set; }

        /// <summary>CPUs to use (e.g., "0-3", "0,1").</summary>
        public string CpusetCpus { get; set; }

        /// <summary>Restart policy.</summary>
        public string RestartPolicy { get; set; }

        /// <summary>Pids limit.</summary>
        public long? PidsLimit { get; set; }
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
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        /// <summary>Limit number of results.</summary>
        public int? Limit { get; set; }
    }

    #endregion
}
