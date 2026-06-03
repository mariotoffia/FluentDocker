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
    /// Container name.
    /// </summary>
    new string Name { get; }

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
    /// Copies data to the container.
    /// </summary>
    /// <param name="containerPath">Destination path in the container.</param>
    /// <param name="data">Data to copy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CopyToAsync(string containerPath, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a file or directory from the host to the container.
    /// </summary>
    /// <param name="hostPath">Source path on the host (file or directory).</param>
    /// <param name="containerPath">Destination path in the container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CopyToAsync(string hostPath, string containerPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a file or directory from the container to the host.
    /// </summary>
    /// <param name="containerPath">Source path in the container.</param>
    /// <param name="hostPath">Destination path on the host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CopyFromToPathAsync(string containerPath, string hostPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets real-time stats from the container.
    /// </summary>
    Task<ContainerStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the container by sending it a signal (defaults to <c>SIGKILL</c>).
    /// Unlike <see cref="IServiceAsync.StopAsync"/>, kill does not give the container a
    /// graceful shutdown period — it is the fast, forceful teardown typically wanted for
    /// disposable integration-test containers.
    /// </summary>
    /// <param name="signal">The signal to send, e.g. <c>SIGKILL</c> or <c>SIGTERM</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task KillAsync(string signal = "SIGKILL", CancellationToken cancellationToken = default);
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

