using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Service operation tests: inspect, logs, exec, start/stop/pause/remove, stats.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ContainerServiceTests
  {
    #region Service Operation Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InspectAsync_CallsDriverAndReturnsContainer()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerInspect("test-container-123", running: true);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        var container = await service.InspectAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(container);
        Assert.Equal("test-container-123", container.Id);
        Assert.True(container.State.Running);
        mockPack.ContainerDriver.Verify(d => d.InspectAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetLogsAsync_CallsDriverAndReturnsLogs()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerGetLogs("Application started\nListening on port 80");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        var logs = await service.GetLogsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Application started", logs);
        Assert.Contains("Listening on port 80", logs);
        mockPack.ContainerDriver.Verify(d => d.GetLogsAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<bool>(),
            It.IsAny<int?>(),
            It.IsAny<bool>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_CallsDriverWithCommand()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerExec("file1.txt\nfile2.txt", 0);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        var result = await service.ExecuteAsync("ls -la", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("file1.txt", result);
        mockPack.ContainerDriver.Verify(d => d.ExecAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.Is<ExecConfig>(c => c.Command.Length == 2 && c.Command[0] == "ls"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StartAsync_CallsDriverAndUpdatesState()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Running, service.State);
        mockPack.ContainerDriver.Verify(d => d.StartAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StopAsync_CallsDriverAndUpdatesState()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        await service.StopAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Stopped, service.State);
        mockPack.ContainerDriver.Verify(d => d.StopAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<int?>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task KillAsync_CallsDriverWithSignalAndUpdatesState()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerKill();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        await service.KillAsync("SIGTERM", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Stopped, service.State);
        mockPack.ContainerDriver.Verify(d => d.KillAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.Is<string>(sig => sig == "SIGTERM"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task KillAsync_DefaultsToSigkill()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerKill();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        await service.KillAsync(cancellationToken: TestContext.Current.CancellationToken);

        mockPack.ContainerDriver.Verify(d => d.KillAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.IsAny<string>(),
            It.Is<string>(sig => sig == "SIGKILL"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task KillAsync_DriverFailure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ContainerDriver
          .Setup(d => d.KillAsync(
              It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<System.Threading.CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<FluentDocker.Model.Drivers.Unit>.Fail(
              "no such container", "CONTAINER_KILL_FAILED"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        await Assert.ThrowsAsync<FluentDocker.Common.DriverException>(
            () => service.KillAsync(cancellationToken: TestContext.Current.CancellationToken));
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PauseAsync_CallsDriverAndUpdatesState()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerPause();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        await service.PauseAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Paused, service.State);
        mockPack.ContainerDriver.Verify(d => d.PauseAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RemoveAsync_CallsDriverAndUpdatesState()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container",
          deleteVolumeOnDispose: true);

      try
      {
        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Removed, service.State);
        mockPack.ContainerDriver.Verify(d => d.RemoveAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<bool>(),
            It.Is<bool>(v => v == true),  // removeVolumes should be true
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatsAsync_CallsDriverAndReturnsStats()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStats(
          cpuPercent: 25.5,
          memoryUsage: 104857600,   // 100 MiB
          memoryLimit: 1073741824,  // 1 GiB
          memoryPercent: 9.77,
          networkRx: 1024000,
          networkTx: 512000,
          blockRead: 2048000,
          blockWrite: 1024000,
          pids: 5);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

      try
      {
        // Act
        var stats = await service.GetStatsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(stats);
        Assert.Equal("test-container-123", stats.ContainerId);

        // CPU stats
        Assert.NotNull(stats.Cpu);
        Assert.Equal(25.5, stats.Cpu.UsagePercent);

        // Memory stats
        Assert.NotNull(stats.Memory);
        Assert.Equal(104857600, stats.Memory.Usage);
        Assert.Equal(1073741824, stats.Memory.Limit);
        Assert.Equal(9.77, stats.Memory.UsagePercent);

        // Network stats
        Assert.NotNull(stats.Network);
        Assert.Equal(1024000, stats.Network.RxBytes);
        Assert.Equal(512000, stats.Network.TxBytes);

        // Disk stats
        Assert.NotNull(stats.Disk);
        Assert.Equal(2048000, stats.Disk.ReadBytes);
        Assert.Equal(1024000, stats.Disk.WriteBytes);

        // Verify driver was called
        mockPack.ContainerDriver.Verify(d => d.StatsAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<string>(s => s == "test-container-123"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion
  }
}
