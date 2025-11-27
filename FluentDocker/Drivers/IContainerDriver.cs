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
    /// </summary>
    public interface IContainerDriver
    {
        /// <summary>
        /// Creates a new container.
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
        /// Removes a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="force">Force removal even if running</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Model.Drivers.CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            string containerId,
            bool force = false,
            CancellationToken cancellationToken = default);

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
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Container logs</returns>
        Task<Model.Drivers.CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            string containerId,
            bool follow = false,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a container create operation.
    /// </summary>
    public class ContainerCreateResult
    {
        /// <summary>
        /// Container ID.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Warnings from the create operation.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for creating a container.
    /// </summary>
    public class ContainerCreateConfig
    {
        /// <summary>
        /// Image to use for the container.
        /// </summary>
        public string Image { get; set; }

        /// <summary>
        /// Container name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Command to run.
        /// </summary>
        public string[] Command { get; set; }

        /// <summary>
        /// Environment variables.
        /// </summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Port bindings (container port -> host port).
        /// </summary>
        public Dictionary<string, string> PortBindings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Volume bindings (host path -> container path or volume name).
        /// </summary>
        public Dictionary<string, string> Volumes { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Network mode.
        /// </summary>
        public string NetworkMode { get; set; }

        /// <summary>
        /// Additional labels.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Working directory inside the container.
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// User to run as inside the container.
        /// </summary>
        public string User { get; set; }

        /// <summary>
        /// Restart policy (no, always, unless-stopped, on-failure).
        /// </summary>
        public string RestartPolicy { get; set; }

        /// <summary>
        /// Hostname of the container.
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// Networks to attach the container to.
        /// </summary>
        public List<string> Networks { get; set; } = new List<string>();

        /// <summary>
        /// Memory limit in bytes.
        /// </summary>
        public long? MemoryLimit { get; set; }

        /// <summary>
        /// CPU shares (relative weight).
        /// </summary>
        public long? CpuShares { get; set; }

        /// <summary>
        /// Whether to run in privileged mode.
        /// </summary>
        public bool Privileged { get; set; }

        /// <summary>
        /// Whether to auto-remove container when it exits.
        /// </summary>
        public bool AutoRemove { get; set; }
    }

    /// <summary>
    /// Filter parameters for listing containers.
    /// </summary>
    public class ContainerListFilter
    {
        /// <summary>
        /// Include all containers (default: only running).
        /// </summary>
        public bool All { get; set; }

        /// <summary>
        /// Filter by status.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Filter by label.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }
}
