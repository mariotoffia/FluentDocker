using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ResourceBaseConcurrencyTests : IAsyncLifetime
  {
    private FluentDockerKernel _kernel = null!;

    public async ValueTask InitializeAsync()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      _kernel = kernel;
    }

    public async ValueTask DisposeAsync()
    {
      GC.SuppressFinalize(this);
      if (_kernel != null)
        await _kernel.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentInitializeAsync_OnlyProvisionsOnce()
    {
      var provisionCount = 0;
      var provisionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

      var resource = new ConcurrencyTestResource(_kernel, onProvision: async ct =>
      {
        Interlocked.Increment(ref provisionCount);
        await provisionTcs.Task;
      });

      // Start two concurrent init calls
      var init1 = resource.InitializeAsync(TestContext.Current.CancellationToken);
      var init2 = resource.InitializeAsync(TestContext.Current.CancellationToken);

      // Let provisioning complete
      await Task.Delay(50, TestContext.Current.CancellationToken);
      provisionTcs.SetResult();

      await init1;
      await init2;

      Assert.True(resource.IsInitialized);
      Assert.Equal(1, provisionCount);
    }

    [Fact]
    public async Task DisposeAsync_DuringInitializeAsync_WaitsForInit()
    {
      var provisionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var teardownCalled = false;

      var resource = new ConcurrencyTestResource(_kernel,
          onProvision: async ct => await provisionTcs.Task,
          onTeardown: ct =>
          {
            teardownCalled = true;
            return Task.CompletedTask;
          });

      var initTask = resource.InitializeAsync(TestContext.Current.CancellationToken);

      // Start dispose while init is in progress
      await Task.Delay(50, TestContext.Current.CancellationToken);
      var disposeTask = resource.DisposeAsync().AsTask();

      // Dispose should be blocked (init holds the lock)
      await Task.Delay(50, TestContext.Current.CancellationToken);
      Assert.False(disposeTask.IsCompleted);

      // Complete provisioning
      provisionTcs.SetResult();
      await initTask;
      await disposeTask;

      Assert.True(teardownCalled);
      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task ConcurrentDisposeAsync_OnlyTearsDownOnce()
    {
      var teardownCount = 0;

      var resource = new ConcurrencyTestResource(_kernel,
          onTeardown: ct =>
          {
            Interlocked.Increment(ref teardownCount);
            return Task.CompletedTask;
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var dispose1 = resource.DisposeAsync().AsTask();
      var dispose2 = resource.DisposeAsync().AsTask();

      await Task.WhenAll(dispose1, dispose2);

      Assert.False(resource.IsInitialized);
      Assert.Equal(1, teardownCount);
    }

    [Fact]
    public async Task DisposeAsync_WhenTeardownIgnoresCancellation_ReturnsAfterTeardownTimeout()
    {
      var resource = new ConcurrencyTestResource(
          _kernel,
          options: new DockerResourceOptions { TeardownTimeout = TimeSpan.FromMilliseconds(50) },
          onTeardown: _ => new TaskCompletionSource().Task);

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      await resource.DisposeAsync().AsTask()
          .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionIgnoresCancellation_TimesOut()
    {
      var enteredProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var resource = new ConcurrencyTestResource(
          _kernel,
          options: new DockerResourceOptions { InitializationTimeout = TimeSpan.FromMilliseconds(50) },
          onProvision: _ =>
          {
            enteredProvision.SetResult();
            return releaseProvision.Task;
          });

      var initTask = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

      var completed = await Task.WhenAny(
          initTask,
          Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
      releaseProvision.SetResult();

      if (completed != initTask)
        Assert.Fail("InitializeAsync did not honor InitializationTimeout.");

      await Assert.ThrowsAsync<TimeoutException>(() => initTask);
    }

    [Fact]
    public async Task DisposeAsync_DuringHungProvision_ReturnsWithinTeardownBudget()
    {
      var enteredProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var resource = new ConcurrencyTestResource(
          _kernel,
          options: new DockerResourceOptions
          {
            InitializationTimeout = TimeSpan.FromMinutes(1),
            TeardownTimeout = TimeSpan.FromMilliseconds(50)
          },
          onProvision: _ =>
          {
            enteredProvision.SetResult();
            return releaseProvision.Task;
          });

      var initTask = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

      var disposeTask = resource.DisposeAsync().AsTask();
      var completed = await Task.WhenAny(
          disposeTask,
          Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
      releaseProvision.SetResult();
      await initTask;

      if (completed != disposeTask)
        Assert.Fail("DisposeAsync waited indefinitely for the lifecycle lock.");

      await Assert.ThrowsAsync<TimeoutException>(() => disposeTask);
    }

    [Fact]
    public void GenerateUniqueName_WithLongPrefix_PreservesGuidEntropy()
    {
      var prefix = new string('x', 62);
      var first = ConcurrencyTestResource.MakeUniqueName(prefix);
      var second = ConcurrencyTestResource.MakeUniqueName(prefix);

      Assert.NotEqual(first, second);
      Assert.True(first.Length <= 63);
      Assert.True(second.Length <= 63);
    }

    /// <summary>
    /// Minimal <see cref="ResourceBase"/> subclass for concurrency testing.
    /// </summary>
    private sealed class ConcurrencyTestResource(
        FluentDockerKernel kernel,
        Func<CancellationToken, Task>? onProvision = null,
        Func<CancellationToken, Task>? onTeardown = null,
        DockerResourceOptions? options = null) : ResourceBase(kernel, options ?? new DockerResourceOptions())
    {
      private readonly Func<CancellationToken, Task> _onProvision = onProvision ?? (_ => Task.CompletedTask);
      private readonly Func<CancellationToken, Task> _onTeardown = onTeardown ?? (_ => Task.CompletedTask);

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ProvisionAsync(CancellationToken cancellationToken)
          => _onProvision(cancellationToken);

      protected override Task TeardownAsync(CancellationToken cancellationToken)
          => _onTeardown(cancellationToken);

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      public static string MakeUniqueName(string prefix) => GenerateUniqueName(prefix);
    }
  }
}
