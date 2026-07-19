using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
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
    /// <remarks>
    /// Built-in container services cache inspect data for 500 ms to avoid duplicate daemon
    /// calls during polling. Use
    /// <see cref="Extensions.ServiceExtensions.GetConfigurationAsync(IContainerService, bool, CancellationToken)"/>
    /// with <c>fresh: true</c> when a loop must bypass that short cache.
    /// </remarks>
    Task<Container> InspectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the host endpoint for a container port.
    /// </summary>
    /// <param name="portAndProto">Port and protocol, e.g. <c>5432/tcp</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The endpoint reachable from the test host, or null when not bound.</returns>
    Task<IPEndPoint> ToHostExposedEndpointAsync(
        string portAndProto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the host port for a container port.
    /// </summary>
    /// <param name="portAndProto">Port and protocol, e.g. <c>5432/tcp</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The host port, or 0 when not bound.</returns>
    Task<int> GetHostPortAsync(string portAndProto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets buffered container logs asynchronously.
    /// </summary>
    /// <remarks>
    /// <paramref name="follow"/> is rejected by buffered CLI/API drivers because it blocks until
    /// the container exits. Use a streaming driver API for follow-style log consumption.
    /// </remarks>
    Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command in the container asynchronously.
    /// </summary>
    /// <remarks>
    /// <paramref name="command"/> is split into an argument vector with shell-style quote parsing;
    /// pass the <c>string[]</c> overload to bypass parsing when arguments are already tokenized.
    /// </remarks>
    /// <exception cref="System.FormatException">The command contains an unterminated quoted string.</exception>
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command in the container and returns stdout, stderr, and exit code.
    /// </summary>
    /// <remarks>
    /// A non-zero <see cref="ExecResult.ExitCode"/> is returned, not thrown, so callers can inspect
    /// stderr and decide how to handle process failure. <paramref name="command"/> is split into an
    /// argument vector with shell-style quote parsing; pass the <c>string[]</c> overload to bypass it.
    /// </remarks>
    /// <exception cref="System.FormatException">The command contains an unterminated quoted string.</exception>
    Task<ExecResult> ExecuteDetailedAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command argument vector in the container asynchronously.
    /// </summary>
    Task<string> ExecuteAsync(string[] command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command argument vector and returns stdout, stderr, and exit code.
    /// </summary>
    /// <remarks>
    /// A non-zero <see cref="ExecResult.ExitCode"/> is returned, not thrown, so callers can inspect
    /// stderr and decide how to handle process failure.
    /// </remarks>
    Task<ExecResult> ExecuteDetailedAsync(string[] command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the container filesystem as a tar archive buffered in memory.
    /// </summary>
    /// <remarks>
    /// The whole archive is buffered into a single <see cref="byte"/> array, so this fails with an
    /// <see cref="System.IO.IOException"/> / array-size limit for exports at or above ~2 GB. For large
    /// containers use <see cref="ExportToFileAsync(string, CancellationToken)"/>, which streams to disk.
    /// </remarks>
    Task<byte[]> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the container filesystem as a tar archive written directly to
    /// <paramref name="path"/> (atomic, via a temporary <c>.partial</c> file). Streams to disk with
    /// no in-memory buffering, so it is not subject to the ~2 GB ceiling of
    /// <see cref="ExportAsync(CancellationToken)"/>.
    /// </summary>
    /// <param name="path">Destination file path for the tar archive; parent directories are created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExportToFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a single file from the container as bytes.
    /// </summary>
    /// <remarks>Use <see cref="CopyFromToPathAsync"/> for directories.</remarks>
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

    /// <summary>
    /// Resumes a paused container.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnpauseAsync(CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Container statistics.
  /// </summary>
  public class ContainerStats
  {
    /// <summary>Container identifier.</summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>CPU usage metrics.</summary>
    public CpuStats Cpu { get; set; } = new();

    /// <summary>Memory usage metrics.</summary>
    public MemoryStats Memory { get; set; } = new();

    /// <summary>Network I/O metrics.</summary>
    public NetworkStats Network { get; set; } = new();

    /// <summary>Block I/O metrics.</summary>
    public DiskStats Disk { get; set; } = new();
  }

  /// <summary>
  /// CPU usage metrics.
  /// </summary>
  public class CpuStats
  {
    /// <summary>CPU usage percentage reported by the runtime.</summary>
    public double UsagePercent { get; set; }

    /// <summary>Total system CPU usage when available.</summary>
    public long SystemCpuUsage { get; set; }

    /// <summary>Container CPU usage when available.</summary>
    public long ContainerCpuUsage { get; set; }
  }

  /// <summary>
  /// Memory usage metrics.
  /// </summary>
  public class MemoryStats
  {
    /// <summary>Current memory usage in bytes.</summary>
    public long Usage { get; set; }

    /// <summary>Memory limit in bytes.</summary>
    public long Limit { get; set; }

    /// <summary>Memory usage percentage.</summary>
    public double UsagePercent { get; set; }
  }

  /// <summary>
  /// Network I/O metrics.
  /// </summary>
  public class NetworkStats
  {
    /// <summary>Received bytes.</summary>
    public long RxBytes { get; set; }

    /// <summary>Transmitted bytes.</summary>
    public long TxBytes { get; set; }

    /// <summary>Received packet count when available.</summary>
    public long RxPackets { get; set; }

    /// <summary>Transmitted packet count when available.</summary>
    public long TxPackets { get; set; }
  }

  /// <summary>
  /// Block I/O metrics.
  /// </summary>
  public class DiskStats
  {
    /// <summary>Bytes read from block devices.</summary>
    public long ReadBytes { get; set; }

    /// <summary>Bytes written to block devices.</summary>
    public long WriteBytes { get; set; }
  }
}
