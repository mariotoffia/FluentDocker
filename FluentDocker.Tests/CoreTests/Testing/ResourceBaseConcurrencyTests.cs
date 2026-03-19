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

    /// <summary>
    /// Minimal <see cref="ResourceBase"/> subclass for concurrency testing.
    /// </summary>
    private sealed class ConcurrencyTestResource : ResourceBase
    {
      private readonly Func<CancellationToken, Task> _onProvision;
      private readonly Func<CancellationToken, Task> _onTeardown;

      public ConcurrencyTestResource(
          FluentDockerKernel kernel,
          Func<CancellationToken, Task>? onProvision = null,
          Func<CancellationToken, Task>? onTeardown = null)
          : base(kernel)
      {
        _onProvision = onProvision ?? (_ => Task.CompletedTask);
        _onTeardown = onTeardown ?? (_ => Task.CompletedTask);
      }

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ProvisionAsync(CancellationToken cancellationToken)
          => _onProvision(cancellationToken);

      protected override Task TeardownAsync(CancellationToken cancellationToken)
          => _onTeardown(cancellationToken);

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
  }
}
