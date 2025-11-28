using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// A driver pack is a composite driver that contains multiple individual driver implementations.
    /// It allows grouping of related driver implementations under a single registered entity
    /// and provides SysCtl-style resolution of driver interfaces.
    /// </summary>
    public interface IDriverPack : ISysCtl
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
        /// Checks if the driver pack is available and healthy.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if driver pack is healthy</returns>
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Initializes the driver pack and all contained drivers.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default);
    }
}

