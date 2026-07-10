using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServicesLayerChunk2Tests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ContainerStartAsync_WhenAlreadyRunning_StillInvokesDriverAndRefreshesState()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);
      MockPack.SetupContainerInspect("container-123", running: false);

      await service.StartAsync(TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()), Times.Exactly(2));
      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task ContainerStopAsync_WhenAlreadyStopped_StillInvokesDriver()
    {
      // SVC-MAJ-4: a cached "Stopped" may be stale (external restart), so stop always re-issues to
      // the daemon (which is idempotent) rather than short-circuiting and dropping the intent.
      MockPack.SetupContainerStop();
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StopAsync(TestContext.Current.CancellationToken);

      await service.StopAsync(TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ContainerKillAsync_WhenAlreadyStopped_StillInvokesDriver()
    {
      MockPack.SetupContainerStop();
      MockPack.ContainerDriver
          .Setup(d => d.KillAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StopAsync(TestContext.Current.CancellationToken);

      await service.KillAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.KillAsync(
          It.IsAny<DriverContext>(), "container-123", "SIGKILL",
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContainerUnpauseAsync_WhenAlreadyRunning_StillInvokesDriver()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      MockPack.ContainerDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);

      await service.UnpauseAsync(TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.UnpauseAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NetworkRemoveAsync_WhenCalledTwice_FiresRemovingOnceAndSkipsSecondDriverCall()
    {
      MockPack.SetupNetworkRemove();
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");
      var removing = 0;
      service.StateChange += (_, args) =>
      {
        if (args.State == ServiceRunningState.Removing)
          removing++;
      };

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(1, removing);
      MockPack.NetworkDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VolumeRemoveAsync_WhenCalledTwice_FiresRemovingOnceAndSkipsSecondDriverCall()
    {
      MockPack.SetupVolumeRemove();
      var service = new VolumeService(Kernel, DriverId, "vol-1", "local");
      var removing = 0;
      service.StateChange += (_, args) =>
      {
        if (args.State == ServiceRunningState.Removing)
          removing++;
      };

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(1, removing);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "vol-1", It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImageRemoveAsync_WhenCalledTwice_FiresRemovingOnceAndSkipsSecondDriverCall()
    {
      MockPack.SetupImageRemove();
      var service = new ImageService(Kernel, DriverId, "img-1", "repo", "tag");
      var removing = 0;
      service.StateChange += (_, args) =>
      {
        if (args.State == ServiceRunningState.Removing)
          removing++;
      };

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(1, removing);
      MockPack.ImageDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "img-1", It.IsAny<bool>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PodRemoveAsync_WhenCalledTwice_FiresRemovingOnceAndSkipsSecondDriverCall()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(), "pod", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.RegisterCustomDriver(podDriver.Object);
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");
      var removing = 0;
      service.StateChange += (_, args) =>
      {
        if (args.State == ServiceRunningState.Removing)
          removing++;
      };

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(1, removing);
      podDriver.Verify(d => d.RemovePodAsync(
          It.IsAny<DriverContext>(), "pod", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NetworkRemoveAsync_WhenDriverReportsNotFound_MarksRemoved()
    {
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("not found", ErrorCodes.Network.NotFound));
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task VolumeRemoveAsync_WhenDriverReportsNotFound_MarksRemoved()
    {
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "vol-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("not found", ErrorCodes.Volume.NotFound));
      var service = new VolumeService(Kernel, DriverId, "vol-1", "local");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task ImageRemoveAsync_WhenDriverReportsNotFound_MarksRemoved()
    {
      MockPack.ImageDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "img-1", It.IsAny<bool>(), It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageRemoveResult>.Fail("not found", ErrorCodes.Image.NotFound));
      var service = new ImageService(Kernel, DriverId, "img-1", "repo", "tag");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task PodRemoveAsync_WhenDriverReportsNotFound_MarksRemoved()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(), "pod", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("not found", ErrorCodes.Pod.NotFound));
      MockPack.RegisterCustomDriver(podDriver.Object);
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task ComposeStartAsync_WhenCancelled_ResetsStateToUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task ComposeStopAsync_WhenCancelled_ResetsStateToUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeStopConfig>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          service.StopAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task PodPauseAsync_ThrowsFluentDockerNotSupportedException()
    {
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");

      await Assert.ThrowsAsync<FluentDockerNotSupportedException>(() =>
          service.PauseAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContainerGetStatsAsync_WhenDriverReturnsSuccessWithNullData_ThrowsDriverException()
    {
      MockPack.ContainerDriver
          .Setup(d => d.StatsAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerStatsResult>.Ok(default!));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.GetStatsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRunningContainersAsync_ReturnsServicesWithRunningState()
    {
      MockPack.SetupContainerList(new Container
      {
        Id = "container-123",
        Name = "web",
        Image = "nginx",
        State = new ContainerState { Running = true }
      });
      var service = new HostService(Kernel, DriverId, "host");

      var containers = await service.GetRunningContainersAsync(TestContext.Current.CancellationToken);

      Assert.Single(containers);
      Assert.Equal(ServiceRunningState.Running, containers[0].State);
    }

    [Fact]
    public async Task GetContainersAsync_WhenListedContainerPaused_SeedsPausedState()
    {
      MockPack.SetupContainerList(new Container
      {
        Id = "container-paused",
        Name = "db",
        Image = "postgres",
        State = new ContainerState { Running = false, Status = "Paused" }
      });
      var service = new HostService(Kernel, DriverId, "host");

      var containers = await service.GetContainersAsync(
          all: true, cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(containers);
      Assert.Equal(ServiceRunningState.Paused, containers[0].State);
    }

    [Fact]
    public async Task ContainerStopAsync_WhenAlreadyRemoved_SkipsDriverCall()
    {
      MockPack.SetupContainerStop();
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Removed);

      await service.StopAsync(TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContainerKillAsync_WhenDriverReportsNotRunning_TransitionsToStoppedWithoutThrowing()
    {
      MockPack.ContainerDriver
          .Setup(d => d.KillAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("Container container-123 is not running", exitCode: 1));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.KillAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task ContainerKillAsync_WhenNonLethalSignalLeavesContainerRunning_DoesNotLieAboutStopped()
    {
      // SVC-MAJ-1: a handler-ignored SIGTERM leaves the container running; `docker kill` returns on
      // delivery, so the service must inspect and report Running, not claim Stopped.
      MockPack.ContainerDriver
          .Setup(d => d.KillAsync(
              It.IsAny<DriverContext>(), "container-123", "SIGTERM", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.KillAsync("SIGTERM", cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task ContainerUnpauseAsync_WhenNotPausedAndInspectRunning_TransitionsToRunning()
    {
      MockPack.ContainerDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("Container container-123 is not paused", exitCode: 1));
      MockPack.SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.UnpauseAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task ContainerUnpauseAsync_WhenNotPausedAndInspectExited_TransitionsToStopped()
    {
      MockPack.ContainerDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("Container container-123 is not paused", exitCode: 1));
      MockPack.SetupContainerInspect("container-123", running: false);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.UnpauseAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }
  }
}
