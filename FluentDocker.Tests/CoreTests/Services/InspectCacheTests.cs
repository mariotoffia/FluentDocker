using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Services
{
  /// <summary>
  /// Unit tests for the short-lived InspectAsync result cache in ContainerService.
  /// Verifies TTL behaviour, state-change invalidation, and thread-safety guarantees.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class InspectCacheTests : IAsyncDisposable
  {
    private FluentDockerKernel _kernel = null!; // deferred-init in CreateServiceAsync
    private MockDriverPack _mockPack = null!; // deferred-init in CreateServiceAsync

    private async Task<ContainerService> CreateServiceAsync(
        string containerId = "cache-test-123",
        bool running = true)
    {
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerInspect(containerId, running);

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      return new ContainerService(
          _kernel, "docker", containerId, "nginx:latest", "cache-test");
    }

    /// <summary>
    /// Invokes the private InvalidateInspectCache method via reflection.
    /// The main project is strong-named so InternalsVisibleTo is not available.
    /// </summary>
    private static void InvokeInvalidateInspectCache(ContainerService service)
    {
      var method = typeof(ContainerService).GetMethod(
          "InvalidateInspectCache",
          BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(method);
      method.Invoke(service, []);
    }

    public async ValueTask DisposeAsync()
    {
      if (_kernel != null)
        await _kernel.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // 1. Cached result is returned within TTL
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_WithinTtl_ReturnsCachedResult()
    {
      // Arrange
      var service = await CreateServiceAsync();

      // Act -- two rapid calls, no delay between them
      var first = await service.InspectAsync(TestContext.Current.CancellationToken);
      var second = await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert -- same reference returned, driver called only once
      Assert.Same(first, second);
      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [Fact]
    public async Task InspectAsync_MultipleCalls_WithinTtl_DriverCalledOnce()
    {
      // Arrange
      var service = await CreateServiceAsync();

      // Act -- five calls in rapid succession
      for (var i = 0; i < 5; i++)
      {
        await service.InspectAsync(TestContext.Current.CancellationToken);
      }

      // Assert
      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    // ------------------------------------------------------------------
    // 2. Cache is invalidated after TTL expires
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_AfterTtlExpires_FetchesFreshResult()
    {
      // Arrange -- use a factory so each call returns a new Container instance
      _mockPack = new MockDriverPack();
      var callCount = 0;
      _mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            callCount++;
            return CommandResponse<Container>.Ok(new Container
            {
              Id = $"container-{callCount}",
              Name = "cache-test",
              State = new ContainerState
              {
                Running = callCount > 1,
                Status = callCount > 1 ? "running" : "exited"
              }
            });
          });

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      var service = new ContainerService(
          _kernel, "docker", "cache-test-123", "nginx:latest", "cache-test");

      // Act -- first call populates cache
      var first = await service.InspectAsync(TestContext.Current.CancellationToken);

      // Wait for TTL to expire (add margin to avoid flakiness)
      await Task.Delay(
          (int)ContainerService.InspectCacheTtlMs + 100,
          TestContext.Current.CancellationToken);

      var second = await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert -- driver was called twice (fresh fetch after TTL)
      Assert.NotSame(first, second);
      Assert.Equal("container-1", first.Id);
      Assert.Equal("container-2", second.Id);
    }

    // ------------------------------------------------------------------
    // 3. Cache is invalidated on state change
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_AfterStartAsync_CacheInvalidated()
    {
      // Arrange -- use a factory so each inspect call returns a distinct instance
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerStart();
      var callCount = 0;
      _mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            callCount++;
            return CommandResponse<Container>.Ok(new Container
            {
              Id = $"container-{callCount}",
              Name = "cache-test",
              State = new ContainerState
              {
                Running = callCount > 1,
                Status = callCount > 1 ? "running" : "exited"
              }
            });
          });

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      var service = new ContainerService(
          _kernel, "docker", "cache-test-123", "nginx:latest", "cache-test");

      // Populate cache
      var first = await service.InspectAsync(TestContext.Current.CancellationToken);

      // State change invalidates cache
      await service.StartAsync(TestContext.Current.CancellationToken);

      // Act -- inspect after state change should re-fetch
      var second = await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert -- StartAsync inspects once and does not cache it; the next inspect re-fetches.
      Assert.NotSame(first, second);
      Assert.Equal("container-1", first.Id);
      Assert.Equal("container-3", second.Id);
    }

    [Fact]
    public async Task InspectAsync_AfterStopAsync_CacheInvalidated()
    {
      // Arrange
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerInspect("cache-test-123", running: true);
      _mockPack.SetupContainerStop();

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      var service = new ContainerService(
          _kernel, "docker", "cache-test-123", "nginx:latest", "cache-test");

      // Populate cache
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // State change invalidates cache
      await service.StopAsync(TestContext.Current.CancellationToken);

      // Act
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert
      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
    }

    [Fact]
    public async Task InspectAsync_AfterPauseAsync_CacheInvalidated()
    {
      // Arrange
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerInspect("cache-test-123", running: true);
      _mockPack.SetupContainerPause();

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      var service = new ContainerService(
          _kernel, "docker", "cache-test-123", "nginx:latest", "cache-test");

      // Populate cache
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // State change
      await service.PauseAsync(TestContext.Current.CancellationToken);

      // Act
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert
      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
    }

    [Fact]
    public async Task InspectAsync_AfterRemoveAsync_CacheInvalidated()
    {
      // Arrange
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerInspect("cache-test-123", running: true);
      _mockPack.SetupContainerRemove();

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      var service = new ContainerService(
          _kernel, "docker", "cache-test-123", "nginx:latest", "cache-test");

      // Populate cache
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // State change
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Act
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert
      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
    }

    // ------------------------------------------------------------------
    // 4. Direct invalidation via reflection
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvalidateInspectCache_ViaReflection_ForcesFreshFetch()
    {
      // Arrange
      var service = await CreateServiceAsync();

      // Populate cache
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Manually invalidate private method via reflection
      InvokeInvalidateInspectCache(service);

      // Act
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert -- two driver calls
      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Exactly(2));
    }

    // ------------------------------------------------------------------
    // 5. TTL constant has expected value
    // ------------------------------------------------------------------

    [Fact]
    public void InspectCacheTtlMs_Is500()
    {
      Assert.Equal(500, ContainerService.InspectCacheTtlMs);
    }

    // ------------------------------------------------------------------
    // 6. State change returns fresh data from driver
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_AfterStateChange_ReturnsNewData()
    {
      // Arrange -- set up a sequence of different inspect responses
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerStart();
      var callCount = 0;

      _mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            callCount++;
            return CommandResponse<Container>.Ok(new Container
            {
              Id = $"container-{callCount}",
              Name = "test",
              State = new ContainerState
              {
                Running = callCount > 1,
                Status = callCount > 1 ? "running" : "exited"
              }
            });
          });

      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);

      var service = new ContainerService(
          _kernel, "docker", "test-id", "nginx:latest", "test");

      // Act -- inspect, then start (invalidates), then inspect again
      var first = await service.InspectAsync(TestContext.Current.CancellationToken);
      await service.StartAsync(TestContext.Current.CancellationToken);
      var second = await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert -- different data returned after state-change invalidation
      Assert.Equal("container-1", first.Id);
      Assert.Equal("container-3", second.Id);
    }

    // ------------------------------------------------------------------
    // 7. Inspect updates service state from response
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_UpdatesServiceState_FromResponse()
    {
      // Arrange
      var service = await CreateServiceAsync(running: true);
      Assert.Equal(ServiceRunningState.Unknown, service.State);

      // Act
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Assert -- state updated to Running from inspect response
      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    // ------------------------------------------------------------------
    // 8. Concurrent reads within TTL all see cached result
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_ConcurrentCalls_WithinTtl_MinimizesDriverCalls()
    {
      // Arrange
      var service = await CreateServiceAsync();

      // Seed the cache with one call
      await service.InspectAsync(TestContext.Current.CancellationToken);

      // Act -- fire 10 concurrent inspects while cache is warm
      var tasks = new Task<Container>[10];
      for (var i = 0; i < tasks.Length; i++)
      {
        tasks[i] = service.InspectAsync(TestContext.Current.CancellationToken);
      }
      await Task.WhenAll(tasks);

      // Assert -- all returned the same cached reference; driver called once
      var first = await tasks[0];
      foreach (var t in tasks)
      {
        Assert.Same(first, await t);
      }

      _mockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    // ------------------------------------------------------------------
    // 9. Invalidation racing the cache-apply path (SVC-10)
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvalidateInspectCache_RacingInFlightInspect_NeverServesPreInvalidationResult()
    {
      // SVC-10: InvalidateInspectCache participates in the same lock/versioning as the cache-apply
      // path, so an invalidation can never be overwritten by an inspect result whose driver fetch
      // started before the invalidation. Each round gates the driver so the in-flight fetch
      // provably predates the invalidation, then asserts the follow-up inspect reaches the driver
      // again (i.e. the stale result was not left in the cache).
      _mockPack = new MockDriverPack();
      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);
      var service = new ContainerService(
          _kernel, "docker", "race-test-123", "nginx:latest", "race-test");
      var fetchCount = 0;

      for (var round = 0; round < 100; round++)
      {
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockPack.ContainerDriver
            .Setup(d => d.InspectAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
              Interlocked.Increment(ref fetchCount);
              fetchStarted.TrySetResult();
              await releaseFetch.Task;
              return CommandResponse<Container>.Ok(new Container
              {
                Id = "race-test-123",
                Name = "race-test",
                State = new ContainerState { Running = true, Status = "running" }
              });
            });

        // Cold cache so the gated inspect really fetches (previous round warmed it).
        InvokeInvalidateInspectCache(service);
        var inFlight = service.InspectAsync(TestContext.Current.CancellationToken);
        await fetchStarted.Task;

        var invalidation = Task.Run(() => InvokeInvalidateInspectCache(service), TestContext.Current.CancellationToken);
        releaseFetch.SetResult();
        await Task.WhenAll(inFlight, invalidation);

        var fetchesBeforeVerification = Volatile.Read(ref fetchCount);
        await service.InspectAsync(TestContext.Current.CancellationToken);
        Assert.True(
            Volatile.Read(ref fetchCount) > fetchesBeforeVerification,
            $"Round {round}: a pre-invalidation inspect result was served from the cache.");
      }
    }
  }
}
