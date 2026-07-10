using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
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
  public class ContainerServiceRemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithExecuteOnDisposing_RunsExecBeforeStop()
    {
      var calls = new List<string>();
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<ExecConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback(() => calls.Add("exec"))
          .ReturnsAsync(CommandResponse<ExecResult>.Ok(new ExecResult()));
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .Callback(() => calls.Add("stop"))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.Execute,
              TriggerState = ServiceRunningState.Removing,
              Command = ["sh", "-c", "cleanup"]
            }
          ]);
      await service.StartAsync(TestContext.Current.CancellationToken);
      calls.Clear();

      await service.DisposeAsync();

      Assert.True(calls.Count >= 2);
      Assert.Equal("exec", calls[0]);
      Assert.Equal("stop", calls[1]);
    }

    [Fact]
    public async Task StartAsync_WhenDriverFails_ResetsStateToUnknown()
    {
      MockPack.ContainerDriver
          .Setup(d => d.StartAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("daemon down", ErrorCodes.Api.ConnectionFailed));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await Assert.ThrowsAsync<ContainerStartException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task StopAsync_WhenDriverFails_ResetsStateToUnknown()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("timeout", ErrorCodes.General.Timeout));

      await Assert.ThrowsAsync<DriverException>(() =>
          service.StopAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task KillAsync_WhenDriverFails_ResetsStateToUnknown()
    {
      MockPack.ContainerDriver
          .Setup(d => d.KillAsync(
              It.IsAny<DriverContext>(), "container-123", "SIGKILL",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("kill failed", ErrorCodes.Container.KillFailed));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.KillAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task StopAsync_WhenAlreadyStopped_ReissuesStopToDaemon()
    {
      // SVC-MAJ-4: the cached "Stopped" may be stale, so stop always re-issues to the idempotent
      // daemon instead of dropping the intent; this re-surfaces Stopping->Stopped transitions.
      MockPack.SetupContainerStop();
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StopAsync(TestContext.Current.CancellationToken);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.StopAsync(TestContext.Current.CancellationToken);

      Assert.Equal([ServiceRunningState.Stopping, ServiceRunningState.Stopped], states);
      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task StopAsync_WhenCanceledBeforeDriverCall_PreservesState()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, int?, CancellationToken>((_, _, _, token) =>
              token.ThrowIfCancellationRequested())
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() => service.StopAsync(cts.Token));

      Assert.Equal(ServiceRunningState.Running, service.State);
      Assert.DoesNotContain(ServiceRunningState.Unknown, states);
    }

    [Theory]
    [InlineData(ErrorCodes.Container.NotFound, "No such container")]
    [InlineData(ErrorCodes.Container.RemoveFailed, "Error: No such container: abc123")]
    [InlineData(ErrorCodes.Container.RemoveFailed, "No such container: abc123")]
    [InlineData(ErrorCodes.Container.RemoveFailed, "Error: no container with name or ID abc123 found")]
    public async Task RemoveAsync_WhenContainerAlreadyGone_TreatsNotFoundAsRemoved(
        string errorCode,
        string error)
    {
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              error,
              errorCode));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task RemoveAsync_WhenRemoveFailedForOtherReason_ThrowsAndMarksUnknown()
    {
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              "device or resource busy",
              ErrorCodes.Container.RemoveFailed));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task DisposeAsync_AfterRemoveAsync_DoesNotRemoveAgainOrReplayHooks()
    {
      MockPack.SetupContainerRemove();
      var removingHooks = 0;
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      service.AddHook(ServiceRunningState.Removing, _ =>
      {
        removingHooks++;
        return Task.CompletedTask;
      });

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.DisposeAsync();

      Assert.Equal(ServiceRunningState.Removed, service.State);
      Assert.Equal(1, removingHooks);
      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_AfterFailedStop_AttemptsGracefulStopAgain()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: true, deleteOnDispose: false);
      await service.StartAsync(TestContext.Current.CancellationToken);
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("timeout", ErrorCodes.General.Timeout));
      await Assert.ThrowsAsync<DriverException>(() =>
          service.StopAsync(TestContext.Current.CancellationToken));

      await service.DisposeAsync();

      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DisposeAsync_KeptContainer_WhenPostStopHookFails_DoesNotThrow()
    {
      MockPack.ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, CancellationToken>((_, _, path, _) =>
              File.WriteAllBytes(path, [1, 2, 3]))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: false,
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.Export,
              TriggerState = ServiceRunningState.Removing,
              HostPath = Path.Combine(".out", "kept-bad-export"),
              Explode = true
            }
          ]);

      await service.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WhenStateChangeHandlerThrows_StillStops()
    {
      MockPack.SetupContainerStop();
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      service.StateChange += (_, _) => throw new InvalidOperationException("subscriber failed");

      await service.StopAsync(TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InspectAsync_WhenStateChanges_RaisesStateChange()
    {
      MockPack.SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Contains(ServiceRunningState.Running, states);
    }

    [Fact]
    public async Task InspectAsync_WhenStateChanges_FiresMatchingHookOnce()
    {
      MockPack.SetupContainerInspect("container-123", running: true);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      var runningHooks = 0;
      service.AddHook(ServiceRunningState.Running, _ =>
      {
        runningHooks++;
        return Task.CompletedTask;
      });

      await service.InspectAsync(TestContext.Current.CancellationToken);
      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal(1, runningHooks);
    }

    [Theory]
    [InlineData("restarting", ServiceRunningState.Starting)]
    [InlineData("removing", ServiceRunningState.Removing)]
    [InlineData("dead", ServiceRunningState.Stopped)]
    public async Task InspectAsync_MapsDockerLifecycleStates(string dockerState, ServiceRunningState expected)
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState
            {
              Running = dockerState == "restarting",
              Restarting = dockerState == "restarting",
              Status = dockerState
            }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal(expected, service.State);
    }

    [Fact]
    public async Task InspectAsync_WhenDockerReportsRestartingAndRunning_MapsToStarting()
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
             It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState
            {
              Running = true,
              Restarting = true,
              Status = "restarting"
            }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Starting, service.State);
    }

    [Fact]
    public async Task DisposeAsync_WithLargeCleanupTimeout_AllowsRemovePastFiveSeconds()
    {
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
             It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(),
             It.IsAny<CancellationToken>()))
          .Returns(async (DriverContext _, string _, bool _, bool _, CancellationToken token) =>
          {
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                canceled);
            var delay = Task.Delay(
                TimeSpan.FromMilliseconds(5200),
                TestContext.Current.CancellationToken);
            var completed = await Task.WhenAny(delay, canceled.Task);
            if (completed == canceled.Task)
              token.ThrowIfCancellationRequested();
            await delay;
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: true,
          disposeCleanupTimeout: TimeSpan.FromSeconds(18));

      await service.DisposeAsync();

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task PauseAsync_WhenStateAlreadyPaused_DoesNotRaiseDuplicateStateChange()
    {
      MockPack.SetupContainerPause();
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.PauseAsync(TestContext.Current.CancellationToken);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.PauseAsync(TestContext.Current.CancellationToken);

      Assert.DoesNotContain(ServiceRunningState.Paused, states);
    }

    [Fact]
    public async Task DisposeAsync_WhenStopCompletesAfterBudget_DoesNotRaisePostDisposeStateChange()
    {
      MockPack.SetupContainerStart()
          .SetupContainerInspect("container-123", running: true);
      var stopResponse = new TaskCompletionSource<CommandResponse<Unit>>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .Returns(stopResponse.Task);
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: true, deleteOnDispose: false,
          disposeCleanupTimeout: TimeSpan.FromMilliseconds(10));
      await service.StartAsync(TestContext.Current.CancellationToken);
      var disposeReturned = false;
      var postDisposeEvents = 0;
      service.StateChange += (_, _) =>
      {
        if (disposeReturned)
          postDisposeEvents++;
      };

      await service.DisposeAsync();
      disposeReturned = true;
      stopResponse.SetResult(CommandResponse<Unit>.Ok(Unit.Default));
      await Task.Yield();

      Assert.Equal(0, postDisposeEvents);
    }
  }
}
