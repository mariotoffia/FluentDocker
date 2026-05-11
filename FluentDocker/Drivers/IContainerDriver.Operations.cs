using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Container operations: exec, copy, logs, stats, wait, rename, update, export.
  /// </summary>
  public partial interface IContainerDriver
  {
    #region Wait Operations

    /// <summary>
    /// Waits for a container to stop and returns exit code.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container wait result with exit code</returns>
    Task<Model.Drivers.CommandResponse<ContainerWaitResult>> WaitAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Information Operations (continued)

    /// <summary>
    /// Gets logs from a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="follow">Follow log output</param>
    /// <param name="tail">Number of lines to show from end (null = all)</param>
    /// <param name="timestamps">Show timestamps</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container logs</returns>
    Task<Model.Drivers.CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        string containerId,
        bool follow = false,
        int? tail = null,
        bool timestamps = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets running processes in a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="psOptions">Optional ps command options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container processes</returns>
    Task<Model.Drivers.CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context,
        string containerId,
        string psOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows changes to container's filesystem.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of filesystem changes</returns>
    Task<Model.Drivers.CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets real-time resource usage statistics from a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container statistics</returns>
    Task<Model.Drivers.CommandResponse<ContainerStatsResult>> StatsAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Execution Operations

    /// <summary>
    /// Executes a command in a running container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="config">Exec configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exec result</returns>
    Task<Model.Drivers.CommandResponse<ExecResult>> ExecAsync(
        DriverContext context,
        string containerId,
        ExecConfig config,
        CancellationToken cancellationToken = default);

    #endregion

    #region Copy Operations

    /// <summary>
    /// Copies files to a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="hostPath">Path on the host</param>
    /// <param name="containerPath">Path in the container</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> CopyToAsync(
        DriverContext context,
        string containerId,
        string hostPath,
        string containerPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies files from a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="containerPath">Path in the container</param>
    /// <param name="hostPath">Path on the host</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> CopyFromAsync(
        DriverContext context,
        string containerId,
        string containerPath,
        string hostPath,
        CancellationToken cancellationToken = default);

    #endregion

    #region Export/Update Operations

    /// <summary>
    /// Exports a container to a tar archive.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="outputPath">Output file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> ExportAsync(
        DriverContext context,
        string containerId,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a container.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="newName">New container name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> RenameAsync(
        DriverContext context,
        string containerId,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates container resource limits.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="config">Update configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Model.Drivers.CommandResponse<Unit>> UpdateAsync(
        DriverContext context,
        string containerId,
        ContainerUpdateConfig config,
        CancellationToken cancellationToken = default);

    #endregion
  }

  #region Operation Result Types

  /// <summary>
  /// Result of a container wait operation.
  /// </summary>
  public class ContainerWaitResult
  {
    /// <summary>Exit code from the container.</summary>
    public int ExitCode { get; set; }

    /// <summary>Error message if any.</summary>
    public string Error { get; set; }
  }

  /// <summary>
  /// Result of an exec operation.
  /// </summary>
  public class ExecResult
  {
    /// <summary>Exit code from the command.</summary>
    public int ExitCode { get; set; }

    /// <summary>Standard output from the command.</summary>
    public string StdOut { get; set; }

    /// <summary>Standard error from the command.</summary>
    public string StdErr { get; set; }
  }

  /// <summary>
  /// Represents a container process.
  /// </summary>
  public class ContainerProcesses
  {
    /// <summary>Column titles.</summary>
    public List<string> Titles { get; set; } = [];

    /// <summary>Process rows.</summary>
    public List<List<string>> Processes { get; set; } = [];
  }

  /// <summary>
  /// Represents a filesystem change in a container.
  /// </summary>
  public class FilesystemChange
  {
    /// <summary>Path that changed.</summary>
    public string Path { get; set; }

    /// <summary>Type of change (A=Added, C=Changed, D=Deleted).</summary>
    public string Kind { get; set; }
  }

  /// <summary>
  /// Container resource usage statistics from docker stats command.
  /// </summary>
  public class ContainerStatsResult
  {
    /// <summary>Container ID.</summary>
    public string ContainerId { get; set; }

    /// <summary>Container name.</summary>
    public string Name { get; set; }

    /// <summary>CPU usage percentage.</summary>
    public double CpuPercent { get; set; }

    /// <summary>Memory usage in bytes.</summary>
    public long MemoryUsage { get; set; }

    /// <summary>Memory limit in bytes.</summary>
    public long MemoryLimit { get; set; }

    /// <summary>Memory usage percentage.</summary>
    public double MemoryPercent { get; set; }

    /// <summary>Network bytes received.</summary>
    public long NetworkRxBytes { get; set; }

    /// <summary>Network bytes transmitted.</summary>
    public long NetworkTxBytes { get; set; }

    /// <summary>Block I/O bytes read.</summary>
    public long BlockReadBytes { get; set; }

    /// <summary>Block I/O bytes written.</summary>
    public long BlockWriteBytes { get; set; }

    /// <summary>Number of PIDs (processes) in the container.</summary>
    public int Pids { get; set; }
  }

  #endregion

  #region Operation Config Types

  /// <summary>
  /// Configuration for exec operation.
  /// </summary>
  public class ExecConfig
  {
    /// <summary>Command to execute.</summary>
    public string[] Command { get; set; }

    /// <summary>Working directory inside the container.</summary>
    public string WorkingDir { get; set; }

    /// <summary>Environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>User to run as.</summary>
    public string User { get; set; }

    /// <summary>Whether to run in privileged mode.</summary>
    public bool Privileged { get; set; }

    /// <summary>Allocate a TTY.</summary>
    public bool Tty { get; set; }

    /// <summary>Keep STDIN attached.</summary>
    public bool Interactive { get; set; }

    /// <summary>Detach from command after starting.</summary>
    public bool Detach { get; set; }
  }

  /// <summary>
  /// Configuration for updating container resources.
  /// </summary>
  public class ContainerUpdateConfig
  {
    /// <summary>Memory limit in bytes.</summary>
    public long? MemoryLimit { get; set; }

    /// <summary>Total memory limit (memory + swap) in bytes. Set to -1 for unlimited swap.</summary>
    public long? MemorySwap { get; set; }

    /// <summary>Memory reservation in bytes.</summary>
    public long? MemoryReservation { get; set; }

    /// <summary>CPU shares (relative weight).</summary>
    public long? CpuShares { get; set; }

    /// <summary>CPU period in microseconds.</summary>
    public long? CpuPeriod { get; set; }

    /// <summary>CPU quota in microseconds.</summary>
    public long? CpuQuota { get; set; }

    /// <summary>CPUs to use (e.g., "0-3", "0,1").</summary>
    public string CpusetCpus { get; set; }

    /// <summary>Restart policy.</summary>
    public string RestartPolicy { get; set; }

    /// <summary>Pids limit.</summary>
    public long? PidsLimit { get; set; }
  }

  #endregion
}
