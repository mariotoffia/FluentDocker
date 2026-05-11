using System.Collections.Generic;
using System.Threading;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using Moq;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Compose and extended container setup helpers for MockDriverPack.
  /// </summary>
  public partial class MockDriverPack
  {
    /// <summary>
    /// Sets up ComposeDriver.UpAsync to return success.
    /// </summary>
    public MockDriverPack SetupComposeUp(string projectName = "test-project")
    {
      ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ComposeUpResult>.Ok(
              new ComposeUpResult { ProjectName = projectName }));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.UpAsync to return a specific result.
    /// </summary>
    public MockDriverPack SetupComposeUpAsync(ComposeUpResult result)
    {
      ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ComposeUpResult>.Ok(result));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.DownAsync to return success.
    /// </summary>
    public MockDriverPack SetupComposeDown()
    {
      ComposeDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeDownConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.ListAsync to return services.
    /// </summary>
    public MockDriverPack SetupComposeList(params ComposeServiceInfo[] services)
    {
      ComposeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeListConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<ComposeServiceInfo>>.Ok(
              [.. services]));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.GetLogsAsync to return logs.
    /// </summary>
    public MockDriverPack SetupComposeGetLogs(string logs = "test logs")
    {
      ComposeDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeLogsConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<string>.Ok(logs));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.ExecuteAsync to return output.
    /// </summary>
    public MockDriverPack SetupComposeExecute(string output = "command output")
    {
      ComposeDriver
          .Setup(d => d.ExecuteAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeExecConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<string>.Ok(output));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.ScaleAsync to return success.
    /// </summary>
    public MockDriverPack SetupComposeScale()
    {
      ComposeDriver
          .Setup(d => d.ScaleAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeScaleConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.StartAsync to return success.
    /// </summary>
    public MockDriverPack SetupComposeStart()
    {
      ComposeDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.StopAsync to return success.
    /// </summary>
    public MockDriverPack SetupComposeStop()
    {
      ComposeDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeStopConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ComposeDriver.PauseAsync to return success.
    /// </summary>
    public MockDriverPack SetupComposePause()
    {
      ComposeDriver
          .Setup(d => d.PauseAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.GetLogsAsync to return logs.
    /// </summary>
    public MockDriverPack SetupContainerGetLogs(string logs = "container logs")
    {
      ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<int?>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<string>.Ok(logs));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.ExecAsync to return output.
    /// </summary>
    public MockDriverPack SetupContainerExec(string stdOut = "exec output", int exitCode = 0)
    {
      ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<ExecConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ExecResult>.Ok(
              new ExecResult { StdOut = stdOut, ExitCode = exitCode }));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.ExportAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerExport()
    {
      ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.CopyFromAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerCopyFrom()
    {
      ContainerDriver
          .Setup(d => d.CopyFromAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.CopyToAsync to return success.
    /// </summary>
    public MockDriverPack SetupContainerCopyTo()
    {
      ContainerDriver
          .Setup(d => d.CopyToAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ContainerDriver.StatsAsync to return container statistics.
    /// </summary>
    public MockDriverPack SetupContainerStats(
        double cpuPercent = 25.5,
        long memoryUsage = 104857600,
        long memoryLimit = 1073741824,
        double memoryPercent = 9.77,
        long networkRx = 1024000,
        long networkTx = 512000,
        long blockRead = 2048000,
        long blockWrite = 1024000,
        int pids = 5)
    {
      ContainerDriver
          .Setup(d => d.StatsAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ContainerStatsResult>.Ok(
              new ContainerStatsResult
              {
                ContainerId = "test-container-123",
                Name = "test-container",
                CpuPercent = cpuPercent,
                MemoryUsage = memoryUsage,
                MemoryLimit = memoryLimit,
                MemoryPercent = memoryPercent,
                NetworkRxBytes = networkRx,
                NetworkTxBytes = networkTx,
                BlockReadBytes = blockRead,
                BlockWriteBytes = blockWrite,
                Pids = pids
              }));
      return this;
    }
  }
}
