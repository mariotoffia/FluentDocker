using System;
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

namespace FluentDocker.Tests.CoreTests.Service
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

      // TESTS-3: inject a controllable clock so the TTL is advanced deterministically rather than
      // slept through with a real Task.Delay that races the 500ms wall-clock TTL.
      var fakeTime = new ManualTimeProvider();
      var service = new ContainerService(
          _kernel, "docker", "cache-test-123", "nginx:latest", "cache-test", timeProvider: fakeTime);

      // Act -- first call populates cache
      var first = await service.InspectAsync(TestContext.Current.CancellationToken);

      // Advance the injected clock past the TTL.
      fakeTime.Advance(TimeSpan.FromMilliseconds(ContainerService.InspectCacheTtlMs + 100));

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
    public async Task StopAsync_RacingInFlightInspect_NeverServesPreInvalidationResult()
    {
      // SVC-10: a public state change (StopAsync) invalidates the inspect cache under the same
      // lock/versioning as the cache-apply path (StopAsync -> Stopping -> Stopped, each transition
      // bumping the cache version and clearing the entry). An invalidation can therefore never be
      // overwritten by an inspect result whose driver fetch started before it. Each round gates the
      // driver so the in-flight fetch provably predates the StopAsync invalidation, then asserts the
      // follow-up inspect reaches the driver again (i.e. the stale result was not left in the cache).
      // Driving the race through the public StopAsync op keeps the test on the public contract rather
      // than reflecting into the private InvalidateInspectCache lifecycle method.
      _mockPack = new MockDriverPack();
      _mockPack.SetupContainerStop();
      _kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", _mockPack);
      // Controllable clock so each round can start cold (age the previous round's entry past the TTL)
      // without reflecting into the private invalidation seam. StopAsync never calls the driver's
      // InspectAsync, so it cannot deadlock on the gated inspect mock.
      var fakeTime = new ManualTimeProvider();
      var service = new ContainerService(
          _kernel, "docker", "race-test-123", "nginx:latest", "race-test", timeProvider: fakeTime);
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

        // Age any cache entry the previous round left behind past the TTL so the gated inspect
        // really fetches rather than serving a warm cache.
        fakeTime.Advance(TimeSpan.FromMilliseconds(ContainerService.InspectCacheTtlMs + 100));
        var inFlight = service.InspectAsync(TestContext.Current.CancellationToken);
        await fetchStarted.Task;

        // Public invalidation trigger: StopAsync races the in-flight inspect's cache-apply.
        var invalidation = Task.Run(
            () => service.StopAsync(TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        releaseFetch.SetResult();
        await Task.WhenAll(inFlight, invalidation);

        var fetchesBeforeVerification = Volatile.Read(ref fetchCount);
        await service.InspectAsync(TestContext.Current.CancellationToken);
        Assert.True(
            Volatile.Read(ref fetchCount) > fetchesBeforeVerification,
            $"Round {round}: a pre-invalidation inspect result was served from the cache.");
      }
    }

    // TESTS-3: minimal controllable clock. Microsoft.Extensions.TimeProvider.Testing
    // (FakeTimeProvider) is not referenced by this test project, so a tiny local TimeProvider
    // subclass supplies a timestamp we can advance deterministically past the inspect-cache TTL.
    // TimestampFrequency is ticks/second so GetTimestamp() returns ticks and the base
    // GetElapsedTime math is exact.
    private sealed class ManualTimeProvider : TimeProvider
    {
      private long _timestamp;

      public override long TimestampFrequency => TimeSpan.TicksPerSecond;

      public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

      public void Advance(TimeSpan delta) => Interlocked.Add(ref _timestamp, delta.Ticks);
    }
  }
}
