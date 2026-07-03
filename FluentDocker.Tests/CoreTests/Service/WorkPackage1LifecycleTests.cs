using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for Work Package 1 lifecycle-correctness fixes: borrowed discovered
  /// containers, bounded container dispose, reverse/best-effort BuildResults disposal, and
  /// the obsolete connected-containers accessor.
  /// </summary>
  [Trait("Category", "Unit")]
  public class WorkPackage1LifecycleTests
  {
    #region Issue 1 — discovered containers are borrowed

    [Fact]
    public async Task GetContainersAsync_DiscoveredContainer_DisposeDoesNotStopOrRemove()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerList(
          new Container { Id = "c1", Image = "nginx:latest", Name = "web" });
      mockPack.SetupContainerStop();
      mockPack.SetupContainerRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var host = new HostService(kernel, "docker", "test-host");
        var containers = await host.GetContainersAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(containers);
        await containers[0].DisposeAsync();

        // Borrowed container: disposal must never stop or delete a user's running container.
        mockPack.VerifyContainerStopped(Times.Never());
        mockPack.VerifyContainerRemoved(Times.Never());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Issue 4 — container dispose cleanup is bounded by a timeout

    [Fact]
    public async Task ContainerService_DisposeAsync_HungDaemon_ReturnsWithinTimeout()
    {
      var mockPack = new MockDriverPack();

      // Driver remove never completes and ignores cancellation (simulates a hung daemon).
      var removeCancellationObserved = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var neverCompletes = new TaskCompletionSource<CommandResponse<Unit>>();
      mockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, bool, bool, CancellationToken>(
              (_, _, _, _, ct) =>
              {
                ct.Register(() => removeCancellationObserved.SetResult());
                return neverCompletes.Task;
              });

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(
            kernel, "docker", "cid", "nginx", "web",
            stopOnDispose: false, deleteOnDispose: true,
            disposeCleanupTimeout: TimeSpan.FromMilliseconds(200));

        await service.DisposeAsync();

        await removeCancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Issue 6 — GetConnectedContainersAsync inspects the network for connected containers

    [Fact]
    public async Task GetConnectedContainersAsync_InspectsAndReturnsEmpty()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupNetworkInspect("net-1");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new NetworkService(kernel, "docker", "net-1", "test-network");

        var ids = await service.GetConnectedContainersAsync(TestContext.Current.CancellationToken);

        Assert.Empty(ids);
        mockPack.NetworkDriver.Verify(d => d.InspectAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Issue 3 — BuildResults disposes in reverse order, best-effort

    [Fact]
    public async Task BuildResults_DisposeAsync_DisposesInReverseCreationOrder()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var log = new List<string>();
      var scope = new BuildScope(kernel, "docker");
      scope.AddResult(new RecordingService("a", log));
      scope.AddResult(new RecordingService("b", log));
      scope.AddResult(new RecordingService("c", log));

      var results = new BuildResults([scope]);
      await results.DisposeAsync();

      Assert.Equal(new[] { "c", "b", "a" }, log);
      kernel.Dispose();
    }

    [Fact]
    public async Task BuildResults_DisposeAsync_ContinuesPastThrowingService_DoesNotThrow()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var log = new List<string>();
      var scope = new BuildScope(kernel, "docker");
      scope.AddResult(new RecordingService("a", log));
      scope.AddResult(new RecordingService("b", log, throwOnDispose: true));
      scope.AddResult(new RecordingService("c", log));

      var results = new BuildResults([scope]);

      // Disposal is best-effort and must NOT throw (BuildScope logs per-service failures), so
      // a hung/faulting service cannot fault `await using` or mask the body's exception.
      await results.DisposeAsync();

      // Reverse order: c disposes, b throws (caught + logged), a still disposes.
      Assert.Equal(new[] { "c", "a" }, log);

      kernel.Dispose();
    }

    [Fact]
    public async Task BuildResults_DisposeAsync_IsIdempotent()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var log = new List<string>();
      var scope = new BuildScope(kernel, "docker");
      scope.AddResult(new RecordingService("a", log));

      var results = new BuildResults([scope]);
      await results.DisposeAsync();
      await results.DisposeAsync();

      Assert.Equal(new[] { "a" }, log);
      kernel.Dispose();
    }

    /// <summary>
    /// Minimal <see cref="IServiceAsync"/> that records its disposal in a shared log and can
    /// optionally throw to exercise best-effort disposal.
    /// </summary>
    private sealed class RecordingService(string id, List<string> log, bool throwOnDispose = false)
        : IServiceAsync
    {
      public string Name => id;
      public ServiceRunningState State => ServiceRunningState.Running;
      public FluentDockerKernel Kernel => null!;
      public string DriverId => "docker";

#pragma warning disable CS0067, CS8618
      public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067, CS8618

      public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null!) => this;
      public IServiceAsync RemoveHook(string uniqueName) => this;

      public void Dispose()
      {
        if (throwOnDispose)
          throw new InvalidOperationException($"dispose failed for {id}");
        log.Add(id);
      }

      public ValueTask DisposeAsync()
      {
        if (throwOnDispose)
          throw new InvalidOperationException($"dispose failed for {id}");
        log.Add(id);
        return ValueTask.CompletedTask;
      }
    }

    #endregion
  }
}
