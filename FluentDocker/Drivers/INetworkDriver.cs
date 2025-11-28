using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Network-specific driver operations.
    /// </summary>
    public interface INetworkDriver
    {
        /// <summary>
        /// Creates a network.
        /// </summary>
        Task<CommandResponse<NetworkCreateResult>> CreateAsync(
            DriverContext context,
            NetworkCreateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a network.
        /// </summary>
        Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists networks.
        /// </summary>
        Task<CommandResponse<IList<Network>>> ListAsync(
            DriverContext context,
            NetworkListFilter filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Connects a container to a network.
        /// </summary>
        Task<CommandResponse<Unit>> ConnectAsync(
            DriverContext context,
            string networkId,
            string containerId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnects a container from a network.
        /// </summary>
        Task<CommandResponse<Unit>> DisconnectAsync(
            DriverContext context,
            string networkId,
            string containerId,
            bool force = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects a network to get detailed information.
        /// </summary>
        Task<CommandResponse<Network>> InspectAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Prunes unused networks.
        /// </summary>
        Task<CommandResponse<NetworkPruneResult>> PruneAsync(
            DriverContext context,
            CancellationToken cancellationToken = default);
    }

    public class NetworkCreateConfig
    {
        public string Name { get; set; }
        public string Driver { get; set; } = "bridge";
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
        public string Subnet { get; set; }
        public string Gateway { get; set; }
        public bool EnableIPv6 { get; set; }
        public bool Internal { get; set; }
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }

    public class NetworkCreateResult
    {
        public string Id { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class NetworkListFilter
    {
        public string Name { get; set; }
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }

    public class Network
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Driver { get; set; }
        public string Scope { get; set; }
        public bool IPv6 { get; set; }
        public bool Internal { get; set; }
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }

    public class NetworkPruneResult
    {
        public List<string> NetworksDeleted { get; set; } = new List<string>();
    }
}
