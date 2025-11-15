using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Drivers
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
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<ContainerCreateResult>> CreateAsync(
            DriverContext context,
            ContainerCreateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a container.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="containerId">Container ID or name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<Unit>> StartAsync(
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
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<Unit>> StopAsync(
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
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<Unit>> RemoveAsync(
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
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<Container>> InspectAsync(
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
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<IList<Container>>> ListAsync(
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
        Task<Ductus.FluentDocker.Model.Drivers.CommandResponse<string>> GetLogsAsync(
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
        /// Port bindings.
        /// </summary>
        public Dictionary<string, string> PortBindings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Volume bindings.
        /// </summary>
        public List<string> Volumes { get; set; } = new List<string>();

        /// <summary>
        /// Network mode.
        /// </summary>
        public string NetworkMode { get; set; }

        /// <summary>
        /// Additional labels.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
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
