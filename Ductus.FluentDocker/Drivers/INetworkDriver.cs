using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Model.Drivers;
using Ductus.FluentDocker.Model.Networks;

namespace Ductus.FluentDocker.Drivers
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
    }

    public class NetworkCreateConfig
    {
        public string Name { get; set; }
        public string Driver { get; set; } = "bridge";
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
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
}
