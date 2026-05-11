using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
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
  /// Unit tests for PodService (Podman pod lifecycle management).
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodServiceTests
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
      var kernel = new FluentDockerKernel();
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
      var kernel = new FluentDockerKernel();

      Assert.Throws<ArgumentNullException>(() =>
          new PodService(kernel, null!, "pod-abc123", "my-pod"));

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullPodId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();

      Assert.Throws<ArgumentNullException>(() =>
          new PodService(kernel, "podman", null!, "my-pod"));

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullPodName_DefaultsToPodId()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var podId = "pod-abc123";

      // Act
      var service = new PodService(kernel, "podman", podId, null);

      // Assert — when podName is null, Name falls back to podId
      Assert.Equal(podId, service.Name);

      kernel.Dispose();
    }

    [Fact]
    public void State_InitialValue_IsStopped()
    {
      // Arrange
      var kernel = new FluentDockerKernel();

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
    public async Task StartAsync_Failure_StateRemainsStopped()
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
        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Assert — state should remain Stopped since driver reported failure
        Assert.Equal(ServiceRunningState.Stopped, service.State);
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
    public async Task PauseAsync_ThrowsNotSupportedException()
    {
      // Arrange
      var (kernel, _, _) = await CreateWithPodDriverAsync();
      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod");

      try
      {
        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
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

    #endregion

    #region Hook Tests

    [Fact]
    public void AddHook_StoresHook_RemoveHook_RemovesHook()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
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
      object capturedSender = null;
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

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_WithRemoveOnDispose_RemovesPod()
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

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod",
          removeOnDispose: true);

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert — RemovePodAsync should have been called with force: true
        podDriver.Verify(d => d.RemovePodAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-pod"),
            It.Is<bool>(f => f == true),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_WithoutRemoveOnDispose_DoesNotRemovePod()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod",
          removeOnDispose: false);

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert — RemovePodAsync should NOT have been called
        podDriver.Verify(d => d.RemovePodAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // State should remain Stopped (not Removed)
        Assert.Equal(ServiceRunningState.Stopped, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_RemoveFailure_DoesNotThrow()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("pod removal failed"));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod",
          removeOnDispose: true);

      try
      {
        // Act & Assert — DisposeAsync should swallow the exception
        var exception = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
        Assert.Null(exception);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion
  }
}
