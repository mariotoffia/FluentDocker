using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="ModelService"/>: state transitions, hooks and
  /// unload-on-dispose, mirroring the container/volume service lifecycle.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelServiceTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");

    private static async Task<(FluentDocker.Kernel.FluentDockerKernel kernel, ModelService service)> BuildAsync(bool keepRunning = false)
    {
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .SetupModelRemove()
          .SetupModelInspect(new ModelInfo { Reference = Model })
          .EnableModelDrivers();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning);
      return (kernel, service);
    }

    [Fact]
    public async Task Start_TransitionsToRunning()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        Assert.Equal(ServiceRunningState.Unknown, service.State);
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
    }

    [Fact]
    public async Task Stop_TransitionsToStopped()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Stopped, service.State);
      }
    }

    [Fact]
    public async Task Remove_TransitionsToRemoved()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await service.RemoveAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
    }

    // SVC-1: Removed is terminal. RemoveAsync resets the start-once gate (_loadInitiated), so without
    // the guard a removed model could be silently re-loaded. StartAsync must throw instead.
    [Fact]
    public async Task StartAsync_AfterRemove_ThrowsInvalidOperationException_AndStaysRemoved()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await service.RemoveAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Removed, service.State);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));

        // The terminal state must not be resurrected to Starting/Running.
        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
    }

    // SVC-3: once dispose has completed, the private UpdateState must suppress state changes and
    // StateChange events (mirrors ContainerService). Invoked via reflection to isolate the guard,
    // since the public lifecycle paths bail on ThrowIfDisposed before reaching UpdateState.
    [Fact]
    public async Task UpdateState_AfterDispose_IsSuppressed_NoEventAndNoStateChange()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var events = 0;
        service.StateChange += (_, _) => Interlocked.Increment(ref events);

        await service.DisposeAsync();

        var updateState = typeof(ModelService).GetMethod(
            "UpdateState", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(updateState);
        updateState.Invoke(service, [ServiceRunningState.Running]);

        Assert.Equal(0, Volatile.Read(ref events));
        Assert.NotEqual(ServiceRunningState.Running, service.State);
      }
    }

    // SVC-3: ExecuteHooksAsync must short-circuit once dispose has completed.
    [Fact]
    public async Task ExecuteHooksAsync_AfterDispose_IsSuppressed_HookDoesNotFire()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var fired = 0;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          Interlocked.Increment(ref fired);
          return Task.CompletedTask;
        }, "running");

        await service.DisposeAsync();

        var executeHooks = typeof(ModelService).GetMethod(
            "ExecuteHooksAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeHooks);
        var task = (Task)executeHooks.Invoke(
            service, [ServiceRunningState.Running, CancellationToken.None])!;
        await task;

        Assert.Equal(0, Volatile.Read(ref fired));
      }
    }

    [Fact]
    public async Task Hooks_FireOnStateTransition()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var fired = new List<ServiceRunningState>();
        service.AddHook(ServiceRunningState.Running, _ => { fired.Add(ServiceRunningState.Running); return Task.CompletedTask; });

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ServiceRunningState.Running, fired);
      }
    }

    [Fact]
    public async Task Hooks_MutatedWhileFiring_DoesNotThrow_AndOthersFire()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var fired = new List<string>();
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          fired.Add("self-removing");
          service.RemoveHook("self-removing"); // mutates _stateHooks[Running] while it is being enumerated
          return Task.CompletedTask;
        }, "self-removing");
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          fired.Add("survivor");
          return Task.CompletedTask;
        }, "survivor");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Contains("self-removing", fired);
        Assert.Contains("survivor", fired);
      }
    }

    [Fact]
    public async Task RemoveHook_RemovesOnlyNamedStateRegistration()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var stopped = 0;
        Func<IServiceAsync, Task> hook = _ =>
        {
          stopped++;
          return Task.CompletedTask;
        };
        service.AddHook(ServiceRunningState.Running, hook, "running-hook");
        service.AddHook(ServiceRunningState.Stopped, hook, "stopped-hook");

        service.RemoveHook("running-hook");
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, stopped);
      }
    }

    [Fact]
    public async Task StateChange_EventRaised()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var states = new List<ServiceRunningState>();
        service.StateChange += (_, e) => states.Add(e.State);

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ServiceRunningState.Starting, states);
        Assert.Contains(ServiceRunningState.Running, states);
      }
    }

    [Fact]
    public async Task Pause_NotSupported()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await Assert.ThrowsAsync<FluentDockerNotSupportedException>(() => service.PauseAsync(TestContext.Current.CancellationToken));
      }
    }

    [Fact]
    public async Task Dispose_UnloadsWhenNotKeepRunning()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning: false);

      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.DisposeAsync();

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_KeepRunning_DoesNotUnload()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning: true);

      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.DisposeAsync();

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Never);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task SyncDispose_UnloadsWhenNotKeepRunning_WithoutHanging()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning: false);

      await service.StartAsync(TestContext.Current.CancellationToken);

      // Synchronous Dispose must complete promptly without deadlocking on the
      // thread-pool-dispatched async unload.
      var disposeTask = Task.Run(service.Dispose, TestContext.Current.CancellationToken);
      var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)) == disposeTask;
      Assert.True(completed, "Synchronous Dispose() did not complete in time (possible deadlock).");
      await disposeTask; // surface any exception thrown by Dispose()

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_AfterFailedStart_StillAttemptsUnload()
    {
      // NEW9: a load that initiates then FAILS leaves _state == Unknown (never Running),
      // but the model may already be resident — dispose must still best-effort unload it.
      var pack = new MockDriverPack().SetupModelUnload().EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("load boom", ErrorCodes.Model.LoadFailed));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning: false);

      await Assert.ThrowsAsync<ModelRunnerException>(() => service.StartAsync(TestContext.Current.CancellationToken));
      await service.DisposeAsync();

      // Best-effort unload was attempted even though Start never reached Running.
      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_AfterCanceledStart_DoesNotUnload()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(
          kernel, "docker", Model, runner.Object, null!, keepRunning: false);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      cts.Cancel();

      await Assert.ThrowsAsync<OperationCanceledException>(() => service.StartAsync(cts.Token));
      await service.DisposeAsync();

      runner.Verify(r => r.UnloadAsync(
          It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncDispose_KeepRunning_DoesNotUnload()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning: true);

      await service.StartAsync(TestContext.Current.CancellationToken);
      service.Dispose();

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Never);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task SyncDispose_IsIdempotent()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null!, keepRunning: false);

      await service.StartAsync(TestContext.Current.CancellationToken);

      service.Dispose();
      service.Dispose(); // second call must be a no-op (no second unload)

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Runner_And_Model_Exposed()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        Assert.Equal(Model, service.Model);
        Assert.NotNull(service.Runner);
      }
    }

    [Fact]
    public async Task StartAsync_HoldsGateForFullLoad_NotReleasedEarlyOnCancel()
    {
      // A1: StartAsync must hold the per-model gate for the FULL load. The removed inner
      // .WaitAsync(ct) used to release the gate the instant the token fired while the driver's
      // load was still in flight, letting a concurrent op race. With the fix, the gate stays held
      // until LoadAsync actually returns — so while a load is in progress (here a driver that
      // ignores the token), the SAME model's gate must NOT be acquirable, even after a cancel.
      var model = ModelReference.Parse("ai/gate-" + Guid.NewGuid().ToString("N"));
      var load = new TaskCompletionSource<CommandResponse<Unit>>();
      var enteredLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var pack = new MockDriverPack()
          .SetupModelUnload()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            enteredLoad.SetResult();
            return load.Task;
          }); // ignores the token — simulates a driver mid-load

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), model);
        var service = new ModelService(kernel, "docker", model, runner, null!, false);
        var ct = TestContext.Current.CancellationToken;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var startTask = service.StartAsync(cts.Token);

        // Let StartAsync acquire the gate and enter LoadAsync, then cancel: the OLD code would
        // release the gate here while the load is still running.
        await enteredLoad.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        cts.Cancel();

        // The same model's gate must remain HELD (the load has not returned) — a concurrent
        // acquire must not complete.
        var contender = ModelOperationGate.AcquireAsync(model, ct);
        var raced = await Task.WhenAny(contender, Task.Delay(TimeSpan.FromMilliseconds(200), ct));
        Assert.NotSame(contender, raced);
        Assert.False(contender.IsCompleted);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);

        // Completing the load releases the gate; the contender proceeds.
        load.SetResult(CommandResponse<Unit>.Ok(Unit.Default));
        var handle = await contender.WaitAsync(TimeSpan.FromSeconds(5), ct);
        await handle.DisposeAsync();

        await service.DisposeAsync();
      }
    }

    [Fact]
    public async Task StartAsync_CancelledWaiterAbandonsWaitWithoutCancellingSharedLoad()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var enteredLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var loadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            enteredLoad.TrySetResult();
            await releaseLoad.Task.ConfigureAwait(false);
            loadCompleted.TrySetResult();
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(
          kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      var startTask = service.StartAsync(cts.Token);
      await enteredLoad.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

      cts.Cancel();
      var completed = await Task.WhenAny(
          startTask, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

      Assert.Same(startTask, completed);
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
      Assert.False(releaseLoad.Task.IsCompleted);

      releaseLoad.SetResult();
      await loadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.True(loadCompleted.Task.IsCompletedSuccessfully);
      await service.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentStartAsync_InitiatesLoadOnlyOnce()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var enteredLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var loadCalls = 0;
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            Interlocked.Increment(ref loadCalls);
            enteredLoad.TrySetResult();
            await releaseLoad.Task.ConfigureAwait(false);
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(
          kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      var first = service.StartAsync(TestContext.Current.CancellationToken);
      await enteredLoad.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      var second = service.StartAsync(TestContext.Current.CancellationToken);
      await Task.Delay(100, TestContext.Current.CancellationToken);

      Assert.Equal(1, Volatile.Read(ref loadCalls));

      releaseLoad.SetResult();
      await Task.WhenAll(first, second);
    }
  }
}
