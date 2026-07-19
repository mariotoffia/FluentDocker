using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Extensions;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Container service — exec, copy, logs, stats, and port operations.
  /// </summary>
  public partial class ContainerService
  {
    /// <inheritdoc />
    public async Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (follow)
        throw new FluentDockerNotSupportedException(
            "ContainerService.GetLogsAsync buffers logs and does not support follow=true.");

      return await GetLogsCoreAsync(follow, null, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<string> GetLogsTailAsync(int tail, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      return await GetLogsCoreAsync(follow: false, tail, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetLogsCoreAsync(
        bool follow,
        int? tail,
        CancellationToken cancellationToken)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetLogsAsync(context, _containerId, follow, tail, false, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get logs for container '{_name}': {response.Error}",
            response.ErrorCode ?? ErrorCodes.General.Unknown,
            response.ErrorContext);
      }

      return response.Data!;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var result = await ExecuteDetailedAsync(command, cancellationToken).ConfigureAwait(false);
      return result?.StdOut ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<ExecResult> ExecuteDetailedAsync(string command, CancellationToken cancellationToken = default)
    {
      return await ExecuteDetailedCoreAsync(command, throwIfDisposed: true, cancellationToken)
          .ConfigureAwait(false);
    }

    private async Task<ExecResult> ExecuteDetailedCoreAsync(
        string command,
        bool throwIfDisposed,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ExecConfig
      {
        Command = ShellArgParser.Parse(command)
      };

      var response = await driver.ExecAsync(context, _containerId, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to execute command in container '{_name}': {response.Error}",
            response.ErrorCode ?? ErrorCodes.General.Unknown,
            response.ErrorContext);
      }

      return response.Data!;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string[] command, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var result = await ExecuteDetailedAsync(command, cancellationToken).ConfigureAwait(false);
      return result?.StdOut ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<ExecResult> ExecuteDetailedAsync(string[] command, CancellationToken cancellationToken = default)
    {
      return await ExecuteDetailedCoreAsync(command, throwIfDisposed: true, cancellationToken)
          .ConfigureAwait(false);
    }

    private async Task<ExecResult> ExecuteDetailedCoreAsync(
        string[] command,
        bool throwIfDisposed,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.ExecAsync(context, _containerId,
          new ExecConfig { Command = command }, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to execute command in container '{_name}': {response.Error}",
            response.ErrorCode ?? ErrorCodes.General.Unknown,
            response.ErrorContext);
      }

      return response.Data!;
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
      return await ExportCoreAsync(throwIfDisposed: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ExportCoreAsync(bool throwIfDisposed, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      var tempPath = await ExportToTempFileCoreAsync(throwIfDisposed: false, cancellationToken)
          .ConfigureAwait(false);
      try
      {
        return await File.ReadAllBytesAsync(tempPath, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        DeleteFileBestEffort(tempPath);
      }
    }

    /// <inheritdoc />
    public async Task<byte[]> CopyFromAsync(string containerPath, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var tempRoot = Path.Combine(Path.GetTempPath(), "fluentdocker-copyfrom");
      var tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      var tempPath = Path.Combine(tempDir, "content");
      try
      {
        var response = await driver.CopyFromAsync(context, _containerId, containerPath, tempPath, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to copy from container '{_name}': {response.Error}",
              response.ErrorCode ?? ErrorCodes.General.Unknown,
              response.ErrorContext);
        }

        if (Directory.Exists(tempPath))
        {
          throw new FluentDockerException(
              $"CopyFromAsync returns a single file as bytes, but '{containerPath}' was copied as a directory. " +
              $"Use {nameof(CopyFromToPathAsync)} for directories.");
        }

        return await File.ReadAllBytesAsync(tempPath, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        try
        {
          // ponytail: cleanup is best-effort and must not mask copy success/failure.
          if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
          if (Directory.Exists(tempRoot) && !Directory.EnumerateFileSystemEntries(tempRoot).Any())
            Directory.Delete(tempRoot);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
      }
    }

    /// <summary>
    /// Copies data to a container file.
    /// </summary>
    /// <remarks>
    /// <paramref name="containerPath"/> must be a file path, not a directory; directory
    /// destinations receive a runtime-generated temporary filename.
    /// </remarks>
    public async Task CopyToAsync(string containerPath, byte[] data, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      // Write data to temp file
      var tempPath = Path.GetTempFileName();
      try
      {
        await File.WriteAllBytesAsync(tempPath, data, cancellationToken).ConfigureAwait(false);

        var response = await driver.CopyToAsync(context, _containerId, tempPath, containerPath, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to copy to container '{_name}': {response.Error}",
              response.ErrorCode ?? ErrorCodes.General.Unknown,
              response.ErrorContext);
        }
      }
      finally
      {
        try
        {
          // ponytail: cleanup is best-effort and must not mask copy success/failure.
          if (File.Exists(tempPath))
            File.Delete(tempPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
      }
    }

    /// <summary>
    /// Copies a file or directory from the host to the container.
    /// </summary>
    /// <param name="hostPath">Source path on the host (file or directory).</param>
    /// <param name="containerPath">Destination path in the container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CopyToAsync(string hostPath, string containerPath, CancellationToken cancellationToken = default)
    {
      await CopyToCoreAsync(hostPath, containerPath, throwIfDisposed: true, cancellationToken)
          .ConfigureAwait(false);
    }

    private async Task CopyToCoreAsync(
        string hostPath,
        string containerPath,
        bool throwIfDisposed,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      if (!File.Exists(hostPath) && !Directory.Exists(hostPath))
      {
        throw new FileNotFoundException($"Source path does not exist: {hostPath}");
      }

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.CopyToAsync(context, _containerId, hostPath, containerPath, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to copy to container '{_name}': {response.Error}",
            response.ErrorCode ?? ErrorCodes.General.Unknown,
            response.ErrorContext);
      }
    }

    /// <summary>
    /// Copies a file or directory from the container to the host.
    /// </summary>
    /// <param name="containerPath">Source path in the container.</param>
    /// <param name="hostPath">Destination path on the host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CopyFromToPathAsync(string containerPath, string hostPath, CancellationToken cancellationToken = default)
    {
      await CopyFromToPathCoreAsync(containerPath, hostPath, throwIfDisposed: true, cancellationToken)
          .ConfigureAwait(false);
    }

    private async Task CopyFromToPathCoreAsync(
        string containerPath,
        string hostPath,
        bool throwIfDisposed,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      // Ensure the destination directory exists
      var dir = Path.GetDirectoryName(hostPath);
      if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
      {
        Directory.CreateDirectory(dir);
      }

      var response = await driver.CopyFromAsync(context, _containerId, containerPath, hostPath, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to copy from container '{_name}': {response.Error}",
            response.ErrorCode ?? ErrorCodes.General.Unknown,
            response.ErrorContext);
      }
    }

    /// <inheritdoc />
    public async Task<ContainerStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.StatsAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get stats for container '{_name}': {response.Error}",
            response.ErrorCode ?? ErrorCodes.General.Unknown,
            response.ErrorContext);
      }

      var driverStats = response.Data ?? throw new DriverException(
          $"Failed to get stats for container '{_name}': empty response",
          ErrorCodes.General.Unknown);
      return new ContainerStats
      {
        ContainerId = driverStats.ContainerId ?? string.Empty,
        Cpu = new CpuStats
        {
          UsagePercent = driverStats.CpuPercent,
          SystemCpuUsage = 0, // Not available from docker stats command
          ContainerCpuUsage = 0 // Not available from docker stats command
        },
        Memory = new MemoryStats
        {
          Usage = driverStats.MemoryUsage,
          Limit = driverStats.MemoryLimit,
          UsagePercent = driverStats.MemoryPercent
        },
        Network = new NetworkStats
        {
          RxBytes = driverStats.NetworkRxBytes,
          TxBytes = driverStats.NetworkTxBytes,
          RxPackets = 0, // Not available from docker stats command
          TxPackets = 0 // Not available from docker stats command
        },
        Disk = new DiskStats
        {
          ReadBytes = driverStats.BlockReadBytes,
          WriteBytes = driverStats.BlockWriteBytes
        }
      };
    }

    /// <summary>
    /// Gets the host-exposed endpoint for a container port, using custom resolver if configured.
    /// </summary>
    public async Task<IPEndPoint> ToHostExposedEndpointAsync(
        string portAndProto,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      return (await ServiceEndpointResolver.ResolveAsync(
          this,
          portAndProto,
          _customResolver!,
          GetDockerHostUri(),
          cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Gets the host port for a container port, using custom resolver if configured.
    /// </summary>
    public async Task<int> GetHostPortAsync(string portAndProto, CancellationToken cancellationToken = default)
    {
      var endpoint = await ToHostExposedEndpointAsync(portAndProto, cancellationToken)
          .ConfigureAwait(false);
      return endpoint?.Port ?? 0;
    }

    private Uri GetDockerHostUri()
    {
      return ServiceEndpointResolver.GetDockerHostUri(_kernel.Registry.GetContext(_driverId).Host ?? string.Empty)!;
    }
  }
}
