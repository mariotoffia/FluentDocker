using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ResourceBaseProvisionCleanupTests : IAsyncLifetime
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
    public async Task InitializeAsync_TimedOutLateProvision_IsForceRemoved()
    {
      var enteredProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var forceRemoved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var resource = new LateProvisionResource(
          _kernel,
          enteredProvision,
          releaseProvision,
          forceRemoved,
          new DockerResourceOptions
          {
            InitializationTimeout = TimeSpan.FromMilliseconds(50),
            TeardownTimeout = TimeSpan.FromSeconds(5)
          });

      var init = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<ResourceInitializationException>(() => init);
      Assert.IsType<TimeoutException>(ex.InnerException);

      releaseProvision.SetResult();
      await forceRemoved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Equal(1, resource.ForceRemoveCount);
    }

    [Fact]
    public async Task DisposeAsync_AfterTimedOutProvision_AllowsCleanReinitialize()
    {
      var enteredProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var forceRemoved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var resource = new LateProvisionResource(
          _kernel,
          enteredProvision,
          releaseProvision,
          forceRemoved,
          new DockerResourceOptions
          {
            InitializationTimeout = TimeSpan.FromMilliseconds(50),
            TeardownTimeout = TimeSpan.FromSeconds(5)
          });

      var init = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await Assert.ThrowsAsync<ResourceInitializationException>(() => init);

      // Late provision lands, then dispose grace-awaits it and fences the generation.
      releaseProvision.SetResult();
      await resource.DisposeAsync();

      // Re-initialization must succeed and must not be clobbered by the stale continuation.
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_ExternalCancelDuringProvision_ThrowsCancellation()
    {
      var enteredProvision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var afterReadyCalled = false;
      var resource = new CancellableProvisionResource(
          _kernel,
          enteredProvision,
          () => afterReadyCalled = true);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

      var init = resource.InitializeAsync(cts.Token);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await cts.CancelAsync();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => init);
      Assert.False(afterReadyCalled);
      Assert.False(resource.IsInitialized);
    }

    private sealed class LateProvisionResource(
        FluentDockerKernel kernel,
        TaskCompletionSource enteredProvision,
        TaskCompletionSource releaseProvision,
        TaskCompletionSource forceRemoved,
        DockerResourceOptions options) : ResourceBase(kernel, options)
    {
      public int ForceRemoveCount { get; private set; }

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override async Task ProvisionAsync(CancellationToken cancellationToken)
      {
        enteredProvision.TrySetResult();
        await releaseProvision.Task.ConfigureAwait(false);
        ResourceName = "late-resource";
      }

      protected override Task TeardownAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
      {
        ForceRemoveCount++;
        forceRemoved.TrySetResult();
        return Task.CompletedTask;
      }
    }

    private sealed class CancellableProvisionResource : ResourceBase
    {
      private readonly TaskCompletionSource _enteredProvision;

      public CancellableProvisionResource(
          FluentDockerKernel kernel,
          TaskCompletionSource enteredProvision,
          Action afterReady) : base(kernel)
      {
        _enteredProvision = enteredProvision;
        OnAfterReady(_ =>
        {
          afterReady();
          return Task.CompletedTask;
        });
      }

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override async Task ProvisionAsync(CancellationToken cancellationToken)
      {
        _enteredProvision.SetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
      }

      protected override Task TeardownAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
  }
}
