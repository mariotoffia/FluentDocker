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
    public async Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetLogsAsync(context, _containerId, follow, null, false, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get logs for container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
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
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data?.StdOut;
    }

    public async Task<string> ExecuteAsync(string[] command, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.ExecAsync(context, _containerId,
          new ExecConfig { Command = command }, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to execute command in container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data?.StdOut;
    }

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      // Create a temp file for export
      var tempPath = Path.GetTempFileName();
      try
      {
        var response = await driver.ExportAsync(context, _containerId, tempPath, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to export container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        return await File.ReadAllBytesAsync(tempPath, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        if (File.Exists(tempPath))
          File.Delete(tempPath);
      }
    }

    public async Task<byte[]> CopyFromAsync(string containerPath, CancellationToken cancellationToken = default)
    {
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
              response.ErrorCode,
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
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, recursive: true);
        if (Directory.Exists(tempRoot) && !Directory.EnumerateFileSystemEntries(tempRoot).Any())
          Directory.Delete(tempRoot);
      }
    }

    public async Task CopyToAsync(string containerPath, byte[] data, CancellationToken cancellationToken = default)
    {
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
              response.ErrorCode,
              response.ErrorContext);
        }
      }
      finally
      {
        if (File.Exists(tempPath))
          File.Delete(tempPath);
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
            response.ErrorCode,
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
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task<ContainerStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.StatsAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get stats for container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var driverStats = response.Data;
      return new ContainerStats
      {
        ContainerId = driverStats.ContainerId,
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
      return await ServiceEndpointResolver.ResolveAsync(
          this,
          portAndProto,
          _customResolver,
          GetDockerHostUri(),
          cancellationToken).ConfigureAwait(false);
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
      try
      {
        return ServiceEndpointResolver.GetDockerHostUri(_kernel.Registry.GetContext(_driverId).Host);
      }
      catch (Exception)
      {
        return null;
      }
    }
  }
}
