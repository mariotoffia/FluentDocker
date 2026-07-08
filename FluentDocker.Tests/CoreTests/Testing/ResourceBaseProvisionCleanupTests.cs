using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

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
    public async Task DisposeAsync_WhenAbandonedProvisionOutlivesGrace_StillForceRemovesLateResource()
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
            InitializationTimeout = TimeSpan.FromMilliseconds(20),
            TeardownTimeout = TimeSpan.FromMilliseconds(50)
          });

      var init = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await Assert.ThrowsAsync<ResourceInitializationException>(() => init);

      await resource.DisposeAsync();
      releaseProvision.SetResult();

      await forceRemoved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Equal(1, resource.ForceRemoveCount);
    }

    [Fact]
    public async Task OrphanCleanup_AfterSuccessfulLateForceRemove_PreservesSameNamedCurrentSessionResource()
    {
      // Drive the abandoned-provision force-remove SUCCESS path (mirrors the outlives-grace test):
      // dispose fences the generation, the late provision lands, then ForceRemoveAsync succeeds.
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
            InitializationTimeout = TimeSpan.FromMilliseconds(20),
            TeardownTimeout = TimeSpan.FromMilliseconds(50)
          });

      var init = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await enteredProvision.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await Assert.ThrowsAsync<ResourceInitializationException>(() => init);

      await resource.DisposeAsync();
      releaseProvision.SetResult();
      await forceRemoved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Equal(1, resource.ForceRemoveCount);

      // A later current-session container reuses the same caller-fixed name ("late-resource").
      // Because the late force-remove SUCCEEDED there is nothing left to reap, so the name must NOT
      // be marked for orphan cleanup — otherwise the scan below wrong-reaps the live resource.
      var (orphanKernel, pack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      try
      {
        pack.SetupContainerList(new Container
        {
          Id = "live-1",
          Name = "late-resource",
          Config = new ContainerConfig
          {
            Labels = new Dictionary<string, string>
            {
              [SessionLabel.Key] = "current-session-id",
              [SessionLabel.ManagedKey] = "true"
            }
          }
        })
            .SetupContainerRemove()
            .SetupNetworkList();
        pack.VolumeDriver
            .Setup(d => d.ListAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<VolumeListFilter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<IList<FluentDocker.Model.Volumes.Volume>>.Ok([]));

        await OrphanCleanup.CleanupOrphanedResourcesAsync(
            orphanKernel, "docker", "current-session-id", TestContext.Current.CancellationToken);

        pack.ContainerDriver.Verify(
            d => d.RemoveAsync(
                It.IsAny<DriverContext>(), "live-1",
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "A successful late force-remove must not leave the name marked for orphan reaping.");
      }
      finally
      {
        await orphanKernel.DisposeAsync();
      }
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
