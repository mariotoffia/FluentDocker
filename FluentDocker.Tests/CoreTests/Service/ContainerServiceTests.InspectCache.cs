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

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");

        await service.InspectAsync(TestContext.Current.CancellationToken);

        // Wait longer than the cache TTL (500ms)
        await Task.Delay(600, TestContext.Current.CancellationToken);

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
  }
}
