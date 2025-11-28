using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Services
{
    /// <summary>
    /// Async network service interface.
    /// </summary>
    public interface INetworkService : IServiceAsync
    {
        /// <summary>
        /// Network ID.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Network name.
        /// </summary>
        string NetworkName { get; }

        /// <summary>
        /// Connects a container to this network.
        /// </summary>
        Task ConnectAsync(string containerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnects a container from this network.
        /// </summary>
        Task DisconnectAsync(string containerId, bool force = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the list of containers connected to this network.
        /// </summary>
        Task<IList<string>> GetConnectedContainersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects the network to get detailed information.
        /// </summary>
        Task<Drivers.Network> InspectAsync(CancellationToken cancellationToken = default);
    }
}

