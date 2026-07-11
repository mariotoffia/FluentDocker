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

    // S-H2: `docker compose up/start` returns success even when a service crashes on boot.
    // Pre-fix, StartAsync fired Running hooks right after the optimistic UpdateState(Running),
    // before reconcile ever ran, so hooks ran against a dead stack (this assertion was RED:
    // hookCalled was true). Post-fix mirrors the already-correct RestartAsync (SVC-MAJ-3):
    // reconcile runs first and hooks only fire if reconcile confirms Running.
    [Fact]
    public async Task ComposeStartAsync_ReconcileFindsStackDead_RunningHooksDoNotFireAndStateReflectsReconcile()
    {
      MockPack.SetupComposeStart();
      MockPack.SetupComposeList(new ComposeServiceInfo { Name = "web", State = "exited" });
      var service = new ComposeService(Kernel, DriverId, [], "project");
      var runningHookCalled = false;
      service.AddHook(ServiceRunningState.Running, _ =>
      {
        runningHookCalled = true;
        return Task.CompletedTask;
      }, "running-hook");

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.False(runningHookCalled);
      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task ComposeStartAsync_ReconcileConfirmsRunning_RunningHooksFire()
    {
      MockPack.SetupComposeStart();
      MockPack.SetupComposeList(new ComposeServiceInfo { Name = "web", State = "running" });
      var service = new ComposeService(Kernel, DriverId, [], "project");
      var runningHookCalled = false;
      service.AddHook(ServiceRunningState.Running, _ =>
      {
        runningHookCalled = true;
        return Task.CompletedTask;
      }, "running-hook");

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.True(runningHookCalled);
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task ComposeStopAsync_ReconcileConfirmsStopped_StoppedHooksFire()
    {
      MockPack.SetupComposeStop();
      MockPack.SetupComposeList(new ComposeServiceInfo { Name = "web", State = "exited" });
      var service = new ComposeService(Kernel, DriverId, [], "project");
      var stoppedHookCalled = false;
      service.AddHook(ServiceRunningState.Stopped, _ =>
      {
        stoppedHookCalled = true;
        return Task.CompletedTask;
      }, "stopped-hook");

      await service.StopAsync(TestContext.Current.CancellationToken);

      Assert.True(stoppedHookCalled);
      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    // Mirrors the Start-side guard: `compose stop` can report success while the ps probe still
    // shows the project running (e.g. a restart policy revived it). Pre-fix, Stopped hooks fired
    // unconditionally right after the optimistic UpdateState(Stopped), before reconcile ever ran
    // (this assertion was RED: stoppedHookCalled was true).
    [Fact]
    public async Task ComposeStopAsync_ReconcileFindsStillRunning_StoppedHooksDoNotFire()
    {
      MockPack.SetupComposeStop();
      MockPack.SetupComposeList(new ComposeServiceInfo { Name = "web", State = "running" });
      var service = new ComposeService(Kernel, DriverId, [], "project");
      var stoppedHookCalled = false;
      service.AddHook(ServiceRunningState.Stopped, _ =>
      {
        stoppedHookCalled = true;
        return Task.CompletedTask;
      }, "stopped-hook");

      await service.StopAsync(TestContext.Current.CancellationToken);

      Assert.False(stoppedHookCalled);
      Assert.Equal(ServiceRunningState.Running, service.State);
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
    public async Task PodStartAsync_WhenRemoved_ThrowsAndDoesNotResurrect()
    {
      // SVC-MAJ-2: starting a removed pod must throw (like the container/compose siblings) rather
      // than transition Removed -> Starting -> Unknown and fire hooks against a gone pod.
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(), "pod", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.RegisterCustomDriver(podDriver.Object);
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      Assert.Equal(ServiceRunningState.Removed, service.State);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Removed, service.State);
      podDriver.Verify(d => d.StartPodAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
