using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;

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
            response.ErrorCode);
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
            response.ErrorCode);
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
              response.ErrorCode);
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

      // Create a temp file for copy
      var tempPath = Path.GetTempFileName();
      try
      {
        var response = await driver.CopyFromAsync(context, _containerId, containerPath, tempPath, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to copy from container '{_name}': {response.Error}",
              response.ErrorCode);
        }

        return await File.ReadAllBytesAsync(tempPath, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        if (File.Exists(tempPath))
          File.Delete(tempPath);
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
              response.ErrorCode);
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
            response.ErrorCode);
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
            response.ErrorCode);
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
            response.ErrorCode);
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
    /// Gets the host port for a container port, using custom resolver if configured.
    /// </summary>
    public async Task<int> GetHostPortAsync(string portAndProto, CancellationToken cancellationToken = default)
    {
      var config = await InspectAsync(cancellationToken).ConfigureAwait(false);

      if (_customResolver != null && config?.NetworkSettings?.Ports != null)
      {
        var endpoint = _customResolver(config.NetworkSettings.Ports, portAndProto, null);
        return endpoint?.Port ?? 0;
      }

      if (config?.NetworkSettings?.Ports == null)
        return 0;

      if (!config.NetworkSettings.Ports.TryGetValue(portAndProto, out var bindings) ||
          bindings == null || bindings.Length == 0)
        return 0;

      var binding = bindings[0];
      return int.TryParse(binding.HostPort, out var port) ? port : 0;
    }
  }
}
