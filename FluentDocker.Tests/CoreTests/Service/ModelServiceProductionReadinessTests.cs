using System;
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

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ModelServiceProductionReadinessTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");

    [Fact]
    public async Task StartAsync_AfterHardLoadFailure_RetriesAndLoads()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var calls = 0;
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            if (calls == 1)
              throw new InvalidOperationException("transient load failure");
            return Task.CompletedTask;
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(TestContext.Current.CancellationToken));
      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(2, calls);
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task StopAsync_ModelStateChangeHandlerCanReenterLifecycleWithoutDeadlocking()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.UnloadAsync(It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      // Force the reentrant RemoveAsync continuation onto a pool thread so the reentrant
      // UpdateState(Removed) runs on a DIFFERENT thread than the one holding _stateLock. That
      // cross-thread hop is what turns an under-lock handler invocation into a real deadlock; a
      // synchronous mock hides it behind Monitor reentrancy.
      runner.Setup(r => r.RemoveAsync(It.IsAny<ModelReference>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Returns(async () => await Task.Delay(50, TestContext.Current.CancellationToken));
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);
      await service.StartAsync(TestContext.Current.CancellationToken);
      var reentered = false;
      service.StateChange += (_, args) =>
      {
        if (args.State != ServiceRunningState.Stopped || reentered)
          return;
        reentered = true;
        service.RemoveAsync(force: true, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
      };

      var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
      var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

      Assert.Same(stopTask, completed);
      await stopTask;
      Assert.True(reentered);
      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task StartAsync_AfterStop_ReloadsModel()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var calls = 0;
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            return Task.CompletedTask;
          });
      runner.Setup(r => r.UnloadAsync(It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.StopAsync(TestContext.Current.CancellationToken);
      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(2, calls);
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task StartAsync_ConcurrentCaller_WaitsForWinnerLoad()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var loadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseLoad = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            loadStarted.SetResult(true);
            await releaseLoad.Task.ConfigureAwait(false);
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      var first = service.StartAsync(TestContext.Current.CancellationToken);
      await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      var second = service.StartAsync(TestContext.Current.CancellationToken);

      var winner = await Task.WhenAny(second, Task.Delay(250, TestContext.Current.CancellationToken));
      Assert.NotSame(second, winner);
      Assert.False(second.IsCompleted);

      releaseLoad.SetResult(true);
      await first;
      await second;
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Fact]
    public async Task StartAsync_ConcurrentCaller_ObservesWinnerFailure()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var loadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseLoad = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var failure = new InvalidOperationException("load failed");
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            loadStarted.SetResult(true);
            await releaseLoad.Task.ConfigureAwait(false);
            throw failure;
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      var first = service.StartAsync(TestContext.Current.CancellationToken);
      await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      var second = service.StartAsync(TestContext.Current.CancellationToken);
      releaseLoad.SetResult(true);

      Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => first));
      Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => second));
      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task StartAsync_WinnerTokenCancels_SharedLoadRunsUnderNoneAndOtherWaitersComplete()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var loadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseLoad = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var observed = new CancellationToken?();
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async (ModelReference _, ModelRunOptions __, CancellationToken tok) =>
          {
            observed = tok;
            loadStarted.SetResult(true);
            await releaseLoad.Task.ConfigureAwait(false);
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      using var winnerCts = new CancellationTokenSource();
      var winner = service.StartAsync(winnerCts.Token);
      await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      var loser = service.StartAsync(TestContext.Current.CancellationToken);

      // Cancelling the winner's token abandons only that caller's wait and must NOT poison the
      // shared load: it runs under CancellationToken.None, so the other caller completes once released.
      winnerCts.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => winner);
      Assert.False(loser.IsCompleted);

      releaseLoad.SetResult(true);
      await loser;
      Assert.Equal(ServiceRunningState.Running, service.State);
      Assert.True(observed.HasValue);
      Assert.False(observed.Value.CanBeCanceled);
    }

    [Fact]
    public async Task StartAsync_WhenStateChangeHandlerThrows_IsIsolatedAndStartSucceeds()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      var loads = 0;
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            loads++;
            return Task.CompletedTask;
          });
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      void Handler(object sender, StateChangeEventArgs args)
      {
        if (args.State == ServiceRunningState.Starting)
          throw new InvalidOperationException("state-change handler blew up");
      }
      service.StateChange += Handler;

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Running, service.State);
      Assert.Equal(1, loads);
    }

    [Fact]
    public async Task DisposeAsync_WhenRunnerDisposeThrows_DoesNotThrow()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.DisposeAsync()).Throws(new InvalidOperationException("dispose failed"));
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      await service.DisposeAsync();
    }

    [Fact]
    public void Dispose_WhenRunnerDisposeThrows_DoesNotThrow()
    {
      using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.DisposeAsync()).Throws(new InvalidOperationException("dispose failed"));
      var service = new ModelService(kernel, "docker", Model, runner.Object, null!, keepRunning: true);

      service.Dispose();
    }

    [Fact]
    public async Task ModelService_Dispose_IsBoundedWhenGateHeld()
    {
      var model = ModelReference.Parse("ai/dispose-gate-" + Guid.NewGuid().ToString("N"));
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), model);
        var service = new ModelService(
            kernel,
            "docker",
            model,
            runner,
            null!,
            keepRunning: false,
            disposeCleanupTimeout: TimeSpan.FromMilliseconds(200));
        await service.StartAsync(TestContext.Current.CancellationToken);
        var heldGate = await ModelOperationGate.AcquireAsync(
            model, TestContext.Current.CancellationToken).ConfigureAwait(false);
        var disposeTask = service.DisposeAsync().AsTask();
        var completed = false;
        try
        {
          completed = await Task.WhenAny(
              disposeTask,
              Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ConfigureAwait(false) == disposeTask;
        }
        finally
        {
          await heldGate.DisposeAsync().ConfigureAwait(false);
        }

        Assert.True(completed, "DisposeAsync did not honor the cleanup timeout while waiting for the model gate.");
        await disposeTask.ConfigureAwait(false);
      }
    }

    [Fact]
    public async Task DisposeAsync_WhenLoadCompletesAfterDispose_DoesNotFireRunning()
    {
      await using var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var runner = new Mock<IModelRunner>();
      runner.Setup(r => r.LoadAsync(
              It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            loadStarted.SetResult();
            await releaseLoad.Task.ConfigureAwait(false);
          });
      runner.Setup(r => r.UnloadAsync(It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
      var service = new ModelService(
          kernel,
          "docker",
          Model,
          runner.Object,
          null!,
          keepRunning: false,
          disposeCleanupTimeout: TimeSpan.FromSeconds(1));
      var disposeCompleted = 0;
      var runningStateChangesAfterDispose = 0;
      var runningHooksAfterDispose = 0;
      service.StateChange += (_, args) =>
      {
        if (args.State == ServiceRunningState.Running && Volatile.Read(ref disposeCompleted) != 0)
          Interlocked.Increment(ref runningStateChangesAfterDispose);
      };
      service.AddHook(ServiceRunningState.Running, _ =>
      {
        if (Volatile.Read(ref disposeCompleted) != 0)
          Interlocked.Increment(ref runningHooksAfterDispose);
        return Task.CompletedTask;
      }, "running");

      var start = service.StartAsync(TestContext.Current.CancellationToken);
      await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await service.DisposeAsync();
      Volatile.Write(ref disposeCompleted, 1);
      releaseLoad.SetResult();
      await start.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

      Assert.Equal(0, Volatile.Read(ref runningStateChangesAfterDispose));
      Assert.Equal(0, Volatile.Read(ref runningHooksAfterDispose));
    }
  }
}
