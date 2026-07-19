using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for the ContainerService inspect cache behavior, including
  /// cache invalidation on state changes and version-based staleness prevention.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ContainerServiceTests
  {
    [Fact]
    public async Task InspectAsync_ReturnsCachedData_WithinTtl()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerInspect("c1", running: true);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");

        // Two rapid calls should hit cache on second call
        await service.InspectAsync(TestContext.Current.CancellationToken);
        await service.InspectAsync(TestContext.Current.CancellationToken);

        mockPack.ContainerDriver.Verify(d => d.InspectAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task InspectAsync_RefreshesCacheAfterTtl()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerInspect("c1", running: true);
      // TESTS-3: drive the inspect-cache TTL off an injected controllable clock so we advance the
      // clock deterministically instead of racing a real 500ms TTL with Task.Delay(600).
      var fakeTime = new ManualTimeProvider();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test", timeProvider: fakeTime);

        await service.InspectAsync(TestContext.Current.CancellationToken);

        // Advance the injected clock past the cache TTL (500ms) — no wall-clock sleep.
        fakeTime.Advance(TimeSpan.FromMilliseconds(ContainerService.InspectCacheTtlMs + 100));

        await service.InspectAsync(TestContext.Current.CancellationToken);

        mockPack.ContainerDriver.Verify(d => d.InspectAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task InspectAsync_JustBeforeTtl_ReturnsCachedData()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerInspect("c1", running: true);
      var fakeTime = new ManualTimeProvider();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test", timeProvider: fakeTime);

        await service.InspectAsync(TestContext.Current.CancellationToken);

        // Advance to just under the TTL: the cached result must still be served.
        fakeTime.Advance(TimeSpan.FromMilliseconds(ContainerService.InspectCacheTtlMs - 1));

        await service.InspectAsync(TestContext.Current.CancellationToken);

        mockPack.ContainerDriver.Verify(d => d.InspectAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task InspectAsync_InvalidatesCache_OnStateChange()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerInspect("c1", running: true);
      mockPack.SetupContainerStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");

        // Populate cache
        await service.InspectAsync(TestContext.Current.CancellationToken);

        // State change should invalidate cache
        await service.StopAsync(TestContext.Current.CancellationToken);

        // This should NOT return cached data from before the stop
        await service.InspectAsync(TestContext.Current.CancellationToken);

        // InspectAsync should have been called twice (not served from cache after stop)
        mockPack.ContainerDriver.Verify(d => d.InspectAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task InspectAsync_ConcurrentCallsAfterStateChange_DoNotReturnStaleData()
    {
      var callCount = 0;
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStop();

      // Setup inspect to return different data each time to detect staleness
      mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            var count = Interlocked.Increment(ref callCount);
            return CommandResponse<Container>.Ok(new Container
            {
              Id = "c1",
              Name = $"inspect-{count}",
              State = new ContainerState
              {
                Running = count == 1, // first call: running, second: not
                Status = count == 1 ? "running" : "exited"
              }
            });
          });

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");

        // First inspect: should show running
        var first = await service.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal("inspect-1", first.Name);

        // Stop invalidates cache
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Second inspect: should NOT return the cached "inspect-1" data
        var second = await service.InspectAsync(TestContext.Current.CancellationToken);
        Assert.Equal("inspect-2", second.Name);
        Assert.False(second.State.Running);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    // TESTS-3: minimal controllable clock. Microsoft.Extensions.TimeProvider.Testing (FakeTimeProvider)
    // is not referenced by this test project, so a tiny local TimeProvider subclass supplies a
    // timestamp we can advance deterministically past the inspect-cache TTL. TimestampFrequency is
    // ticks/second so GetTimestamp() returns ticks and the base GetElapsedTime math is exact.
    private sealed class ManualTimeProvider : TimeProvider
    {
      private long _timestamp;

      public override long TimestampFrequency => TimeSpan.TicksPerSecond;

      public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

      public void Advance(TimeSpan delta) => Interlocked.Add(ref _timestamp, delta.Ticks);
    }
  }
}
