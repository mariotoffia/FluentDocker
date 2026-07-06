using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
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
    public async Task StartAsync_WinnerTokenCancels_SharedLoadRunsUnderNoneAndAllComplete()
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

      // Cancelling the winner's token must NOT poison the shared load: it runs under
      // CancellationToken.None, so both callers still complete once the load releases.
      winnerCts.Cancel();
      Assert.False(winner.IsCompleted);
      Assert.False(loser.IsCompleted);

      releaseLoad.SetResult(true);
      await winner;
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
  }
}
