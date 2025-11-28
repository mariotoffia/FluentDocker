using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;

namespace FluentDocker.Services
{
    /// <summary>
    /// Async container service interface.
    /// </summary>
    public interface IContainerService : IServiceAsync
    {
        /// <summary>
        /// Container ID.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Container image.
        /// </summary>
        string Image { get; }

        /// <summary>
        /// Gets detailed container information asynchronously.
        /// </summary>
        Task<Container> InspectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets container logs asynchronously.
        /// </summary>
        Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a command in the container asynchronously.
        /// </summary>
        Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports the container filesystem as a tar archive.
        /// </summary>
        Task<byte[]> ExportAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies a file from the container.
        /// </summary>
        Task<byte[]> CopyFromAsync(string containerPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies a file to the container.
        /// </summary>
        Task CopyToAsync(string containerPath, byte[] data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets real-time stats from the container.
        /// </summary>
        Task<ContainerStats> GetStatsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Container statistics.
    /// </summary>
    public class ContainerStats
    {
        public string ContainerId { get; set; }
        public CpuStats Cpu { get; set; }
        public MemoryStats Memory { get; set; }
        public NetworkStats Network { get; set; }
        public DiskStats Disk { get; set; }
    }

    public class CpuStats
    {
        public double UsagePercent { get; set; }
        public long SystemCpuUsage { get; set; }
        public long ContainerCpuUsage { get; set; }
    }

    public class MemoryStats
    {
        public long Usage { get; set; }
        public long Limit { get; set; }
        public double UsagePercent { get; set; }
    }

    public class NetworkStats
    {
        public long RxBytes { get; set; }
        public long TxBytes { get; set; }
        public long RxPackets { get; set; }
        public long TxPackets { get; set; }
    }

    public class DiskStats
    {
        public long ReadBytes { get; set; }
        public long WriteBytes { get; set; }
    }
}

