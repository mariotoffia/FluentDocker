using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Base interface for all container runtime drivers (Docker, Podman, etc.).
    /// </summary>
    public interface IDriver
    {
        /// <summary>
        /// Gets the driver type (DockerCli, DockerApi, PodmanCli, etc.).
        /// </summary>
        DriverType Type { get; }

        /// <summary>
        /// Gets the runtime type (Docker, Podman, etc.).
        /// </summary>
        RuntimeType Runtime { get; }

        /// <summary>
        /// Gets the driver's capabilities.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Driver capabilities</returns>
        Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the driver is available and healthy.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if driver is healthy</returns>
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Initializes the driver.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default);
    }
}
