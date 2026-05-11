using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for ComposeService failure paths, state transitions,
  /// and extended operation behavior.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ComposeServiceOperationsTests
  {
    private static ComposeService CreateService(
        FluentDockerKernel kernel,
        bool removeVolumes = false,
        bool removeImages = false)
    {
      return new ComposeService(
          kernel, "docker",
          new List<string> { "docker-compose.yml" },
          "test-project",
          removeVolumes: removeVolumes,
          removeImages: removeImages);
    }

    #region ListServicesAsync Failure

    [Fact]
    public async Task ListServicesAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeListConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<ComposeServiceInfo>>.Fail("list failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.ListServicesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("list compose services", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region GetLogsAsync

    [Fact]
    public async Task GetLogsAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeLogsConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Fail("logs failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetLogsAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("logs", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetLogsAsync_WithFollow_PassesFollowFlag()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeLogsConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Ok("streaming logs"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var logs = await service.GetLogsAsync(
            follow: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("streaming logs", logs);
        mockPack.ComposeDriver.Verify(d => d.GetLogsAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeLogsConfig>(c => c.Follow),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region ExecuteAsync Failure

    [Fact]
    public async Task ExecuteAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.ExecuteAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeExecConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Fail("exec failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.ExecuteAsync(
                "web", new[] { "ls" },
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("execute command", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region ScaleAsync Failure

    [Fact]
    public async Task ScaleAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.ScaleAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeScaleConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("scale failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.ScaleAsync(
                "web", 10,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("scale service", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region StartAsync

    [Fact]
    public async Task StartAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("start failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("start compose project", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StartAsync_AfterStop_SetsStateToRunning()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();
      mockPack.SetupComposeStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Stopped, service.State);

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region PauseAsync

    [Fact]
    public async Task PauseAsync_Success_SetsStateToPaused()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposePause();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.PauseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ServiceRunningState.Paused, service.State);
        mockPack.ComposeDriver.Verify(d => d.PauseAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeFileConfig>(c => c.ProjectName == "test-project"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PauseAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.PauseAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("pause failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.PauseAsync(TestContext.Current.CancellationToken));
        Assert.Contains("pause compose project", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region StopAsync Failure

    [Fact]
    public async Task StopAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeStopConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("stop failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.StopAsync(TestContext.Current.CancellationToken));
        Assert.Contains("stop compose project", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeDownConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.RemoveAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("remove compose project", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveAsync_ForceTrue_SetsRemoveVolumes()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel, removeVolumes: false);
        await service.RemoveAsync(
            force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ServiceRunningState.Removed, service.State);
        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => c.RemoveVolumes),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveAsync_NoForce_NoVolumes_KeepsVolumes()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel, removeVolumes: false);
        await service.RemoveAsync(
            force: false,
            cancellationToken: TestContext.Current.CancellationToken);

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => !c.RemoveVolumes),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveAsync_WithRemoveImages_SetsRemoveImagesToAll()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel, removeImages: true);
        await service.RemoveAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => c.RemoveImages == "all"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveAsync_WithoutRemoveImages_RemoveImagesIsNull()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel, removeImages: false);
        await service.RemoveAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => c.RemoveImages == null),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region IServiceCapabilities

    [Fact]
    public void ServiceCapabilities_ReportsCorrectValues()
    {
      var kernel = new FluentDockerKernel();
      try
      {
        var service = CreateService(kernel);
        var caps = (IServiceCapabilities)service;

        Assert.True(caps.CanStart);
        Assert.True(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    #endregion
  }
}
