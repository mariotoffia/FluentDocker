using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServicesLayerMinorProductionReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task NetworkRemoveAsync_WhenPluginErrorSaysNotFound_DoesNotMarkRemoved()
    {
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("network driver plugin xyz not found"));
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task NetworkRemoveAsync_WhenNoSuchNetwork_MarksRemoved()
    {
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("No such network: net-1"));
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task VolumeRemoveAsync_WhenPluginErrorSaysNotFound_DoesNotMarkRemoved()
    {
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "vol-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("volume driver plugin xyz not found"));
      var service = new VolumeService(Kernel, DriverId, "vol-1", "local");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task VolumeRemoveAsync_WhenNoSuchVolume_MarksRemoved()
    {
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "vol-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("No such volume: vol-1"));
      var service = new VolumeService(Kernel, DriverId, "vol-1", "local");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task NetworkRemoveAsync_WhenDockerCliReportsIdNotFound_MarksRemoved()
    {
      // Real Docker CLI: "network <id> not found" with a generic RemoveFailed code (not the typed NotFound).
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              "Error response from daemon: network net-1 not found", ErrorCodes.Network.RemoveFailed));
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task VolumeRemoveAsync_WhenDockerCliReportsNameNotFound_MarksRemoved()
    {
      // Real Docker CLI: "volume <name> not found" with a generic RemoveFailed code (not the typed NotFound).
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "vol-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              "Error response from daemon: volume vol-1 not found", ErrorCodes.Volume.RemoveFailed));
      var service = new VolumeService(Kernel, DriverId, "vol-1", "local");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task ComposeRemoveAsync_WhenSuccessful_FiresRemovingThenRemoved()
    {
      MockPack.ComposeDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ComposeService(Kernel, DriverId, [], "project");
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal([ServiceRunningState.Removing, ServiceRunningState.Removed], states);
      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task ComposeRemoveAsync_WhenDriverFails_SetsUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed", ErrorCodes.Compose.RemoveFailed));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task ComposePauseAsync_WhenDriverFails_SetsUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.PauseAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("pause failed", ErrorCodes.Compose.PauseFailed));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.PauseAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task ComposeUnpauseAsync_WhenDriverFails_SetsUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("unpause failed", ErrorCodes.Compose.UnpauseFailed));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.UnpauseAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task PodStartAsync_WhenAlreadyCanceled_DoesNotCallDriver()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      MockPack.RegisterCustomDriver(podDriver.Object);
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() => service.StartAsync(cts.Token));

      podDriver.Verify(d => d.StartPodAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
