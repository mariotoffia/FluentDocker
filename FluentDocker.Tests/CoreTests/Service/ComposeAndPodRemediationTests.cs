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
  public class ComposeAndPodRemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ComposeStartAsync_WhenDriverFails_ResetsStateToUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("daemon down", ErrorCodes.Api.ConnectionFailed));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task ComposeStopAsync_WhenDriverFails_ResetsStateToUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeStopConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("timeout", ErrorCodes.General.Timeout));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.StopAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task ComposeRefreshStateAsync_WhenAllServicesPaused_SetsPaused()
    {
      MockPack.SetupComposeList(
          new ComposeServiceInfo { Name = "web", State = "paused" },
          new ComposeServiceInfo { Name = "api", State = "paused" });
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await service.RefreshStateAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Paused, service.State);
    }

    [Fact]
    public async Task ComposeRefreshStateAsync_WhenServiceRestarting_SetsStarting()
    {
      MockPack.SetupComposeList(
          new ComposeServiceInfo { Name = "web", State = "restarting" },
          new ComposeServiceInfo { Name = "api", State = "exited" });
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await service.RefreshStateAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Starting, service.State);
    }

    [Fact]
    public async Task PodStartAsync_WhenDriverFails_PreservesErrorCodeAndMarksTransient()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(), "pod", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("daemon down", ErrorCodes.Api.ConnectionFailed));
      MockPack.RegisterCustomDriver(podDriver.Object);
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");

      var error = await Assert.ThrowsAsync<DriverException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.Api.ConnectionFailed, error.ErrorCode);
      Assert.True(error.IsTransient);
      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task NetworkDisposeAsync_WhenRemoveCompletesAfterBudget_DoesNotRaisePostDisposeStateChange()
    {
      var removeResponse = new TaskCompletionSource<CommandResponse<Unit>>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .Returns(removeResponse.Task);
      var service = new NetworkService(
          Kernel, DriverId, "net-1", "net", true, System.TimeSpan.FromMilliseconds(10));
      var disposeReturned = false;
      var postDisposeEvents = 0;
      service.StateChange += (_, _) =>
      {
        if (disposeReturned)
          postDisposeEvents++;
      };

      await service.DisposeAsync();
      disposeReturned = true;
      removeResponse.SetResult(CommandResponse<Unit>.Ok(Unit.Default));
      await Task.Yield();

      Assert.Equal(0, postDisposeEvents);
    }
  }
}
