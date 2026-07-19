using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class BuildScopeProductionReadinessTests
  {
    [Fact]
    public void Results_ReturnsSnapshot_NotLiveList()
    {
      using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "driver");

      var before = scope.Results;
      scope.AddResult(new TestService(kernel));

      Assert.Empty(before);
      Assert.Single(scope.Results);
    }

    [Fact]
    public async Task DisposeAllAsync_DoesNotClearResultAddedAfterSnapshot()
    {
      using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "driver");
      var first = new BlockingService(kernel);
      scope.AddResult(first);

      var disposing = scope.DisposeAllAsync(TestContext.Current.CancellationToken);
      await first.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
      var addedDuringDispose = new TestService(kernel);
      scope.AddResult(addedDuringDispose);

      first.CompleteDispose.SetResult();
      await disposing.WaitAsync(TestContext.Current.CancellationToken);

      var remaining = Assert.Single(scope.Results);
      Assert.Same(addedDuringDispose, remaining);
    }

    private class TestService(FluentDockerKernel kernel) : IServiceAsync
    {
      public string Name => "test";
      public ServiceRunningState State => ServiceRunningState.Stopped;
      public FluentDockerKernel Kernel { get; } = kernel;
      public string DriverId => "driver";
      public event ServiceDelegates.StateChange StateChange
      {
        add { }
        remove { }
      }
      public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null!) => this;
      public IServiceAsync RemoveHook(string uniqueName) => this;
      public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
      public void Dispose()
      {
      }
    }

    private sealed class BlockingService(FluentDockerKernel kernel) : TestService(kernel)
    {
      public TaskCompletionSource DisposeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteDispose { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

      public override async ValueTask DisposeAsync()
      {
        try
        {
          DisposeStarted.SetResult();
          await CompleteDispose.Task.ConfigureAwait(false);
        }
        finally
        {
          await base.DisposeAsync().ConfigureAwait(false);
        }
      }
    }
  }
}
