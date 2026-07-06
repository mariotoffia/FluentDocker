using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for PodService (Podman pod lifecycle management).
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class PodServiceTests
  {
    private static async Task<(FluentDockerKernel kernel, MockDriverPack mockPack, Mock<IPodmanPodDriver> podDriver)>
        CreateWithPodDriverAsync()
    {
      var mockPack = new MockDriverPack();
      var podDriver = new Mock<IPodmanPodDriver>();
      mockPack.RegisterCustomDriver(podDriver.Object);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      return (kernel, mockPack, podDriver);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_SetsProperties()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var driverId = "podman";
      var podId = "pod-abc123";
      var podName = "my-pod";

      // Act
      var service = new PodService(kernel, driverId, podId, podName);

      // Assert
      Assert.Equal(podName, service.Name);
      Assert.Equal(podId, service.Id);
      Assert.Equal(ServiceRunningState.Stopped, service.State);
      Assert.Equal(kernel, service.Kernel);
      Assert.Equal(driverId, service.DriverId);

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new PodService(null!, "podman", "pod-abc123", "my-pod"));
    }

    [Fact]
    public void Constructor_NullDriverId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      Assert.Throws<ArgumentNullException>(() =>
          new PodService(kernel, null!, "pod-abc123", "my-pod"));

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullPodId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      Assert.Throws<ArgumentNullException>(() =>
          new PodService(kernel, "podman", null!, "my-pod"));

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullPodName_DefaultsToPodId()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var podId = "pod-abc123";

      // Act
      var service = new PodService(kernel, "podman", podId, null!);

      // Assert — when podName is null, Name falls back to podId
      Assert.Equal(podId, service.Name);

      kernel.Dispose();
    }

    [Fact]
    public void State_InitialValue_IsStopped()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      // Act
      var service = new PodService(kernel, "podman", "pod-abc123", "my-pod");

      // Assert
      Assert.Equal(ServiceRunningState.Stopped, service.State);

      kernel.Dispose();
    }

    #endregion

    #region Lifecycle Tests

    [Fact]
    public async Task StartAsync_Success_SetsStateToRunning()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Running, service.State);
        podDriver.Verify(d => d.StartPodAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-pod"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task StartAsync_Failure_ThrowsDriverException()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("pod not found"));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        // Act & Assert — failures must surface, not be silently swallowed.
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Pod.StartFailed, ex.ErrorCode);
        Assert.Contains("my-pod", ex.Message);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task StopAsync_Success_SetsStateToStopped()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      podDriver
          .Setup(d => d.StopPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        // First start the pod so state is Running
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);

        // Act
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Stopped, service.State);
        podDriver.Verify(d => d.StopPodAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-pod"),
            It.Is<int?>(t => t == 10),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task PauseAsync_ThrowsFluentDockerNotSupportedException()
    {
      // Arrange
      var (kernel, _, _) = await CreateWithPodDriverAsync();
      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        // Act & Assert
        await Assert.ThrowsAsync<FluentDockerNotSupportedException>(
            () => service.PauseAsync(TestContext.Current.CancellationToken));
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_Success_SetsStateToRemoved()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        // Act
        await service.RemoveAsync(force: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Removed, service.State);
        podDriver.Verify(d => d.RemovePodAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-pod"),
            It.Is<bool>(f => f == true),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task StopAsync_Failure_ThrowsDriverException()
    {
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StopPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("stop boom"));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.StopAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Pod.StopFailed, ex.ErrorCode);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_Failure_ThrowsDriverException()
    {
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("remove boom"));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.RemoveAsync(force: true, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Pod.RemoveFailed, ex.ErrorCode);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region Hook Tests

    [Fact]
    public void AddHook_StoresHook_RemoveHook_RemovesHook()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var service = new PodService(kernel, "podman", "pod-abc123", "my-pod");
      var hookCalled = false;

      // Act — add hook
      var returnedService = service.AddHook(
          ServiceRunningState.Running,
          async _ => hookCalled = true,
          "test-hook");

      // Assert — AddHook returns the service for fluent chaining
      Assert.Same(service, returnedService);

      // Act — remove hook
      var afterRemove = service.RemoveHook("test-hook");

      // Assert — RemoveHook also returns the service for fluent chaining
      Assert.Same(service, afterRemove);

      // Verify hookCalled is still false (hook was never triggered)
      Assert.False(hookCalled);

      kernel.Dispose();
    }

    [Fact]
    public async Task StartAsync_Success_FiresRegisteredRunningHook()
    {
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");
      var runningFired = false;
      var stoppedFired = false;
      service.AddHook(ServiceRunningState.Running, _ => { runningFired = true; return Task.CompletedTask; });
      service.AddHook(ServiceRunningState.Stopped, _ => { stoppedFired = true; return Task.CompletedTask; });

      try
      {
        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(runningFired);
        // State-accurate: the Stopped hook must NOT fire on a Start transition.
        Assert.False(stoppedFired);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHook_SameUniqueName_FiresOnceAndCanBeRemoved()
    {
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");
      var count = 0;

      // Re-registering the same uniqueName must REPLACE, not append (no double-fire).
      service.AddHook(ServiceRunningState.Running, _ => { count++; return Task.CompletedTask; }, "h1");
      service.AddHook(ServiceRunningState.Running, _ => { count++; return Task.CompletedTask; }, "h1");

      try
      {
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);

        // RemoveHook by name removes exactly that hook so it no longer fires.
        service.RemoveHook("h1");
        count = 0;
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region StateChange Event Tests

    [Fact]
    public async Task StartAsync_Success_FiresStateChangeEvent()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      ServiceRunningState? capturedState = null;
      object? capturedSender = null;
      service.StateChange += (sender, args) =>
      {
        capturedSender = sender;
        capturedState = args.State;
      };

      try
      {
        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedState);
        Assert.Equal(ServiceRunningState.Running, capturedState.Value);
        Assert.Same(service, capturedSender);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion


  }
}
