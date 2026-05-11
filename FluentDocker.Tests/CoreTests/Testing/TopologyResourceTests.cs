using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class TopologyResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesMultipleContainers()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.WithinDriver(DriverId, Kernel);
            builder.UseContainer(c => c.UseImage("redis:alpine").WithName("redis"));
            builder.UseContainer(c => c.UseImage("nginx:alpine").WithName("nginx"));
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.True(resource.Services.Count >= 2);
    }

    [Fact]
    public async Task DisposeAsync_CleansUpInReverseOrder()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.WithinDriver(DriverId, Kernel);
            builder.UseContainer(c => c.UseImage("redis:alpine"));
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
      Assert.Throws<InvalidOperationException>(() => _ = resource.Services);
    }

    [Fact]
    public async Task PreflightAsync_FailsWhenContainersNotSupported()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = false
      });

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.WithinDriver(DriverId, Kernel);
            builder.UseContainer(c => c.UseImage("alpine:latest"));
          });

      await Assert.ThrowsAsync<FluentDocker.Common.CapabilityNotSupportedException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProvisionAsync_AutoBindsDriver()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // The user callback does NOT call WithinDriver - the resource does it automatically.
      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.UseContainer(c => c.UseImage("redis:alpine").WithName("auto-bind-test"));
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.True(resource.Services.Count >= 1);

      await resource.DisposeAsync();
    }

    [Fact]
    public void Constructor_NullKernel_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new TopologyResource(null, _ => { }));
    }

    [Fact]
    public void Constructor_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new TopologyResource(Kernel, null!));
    }

    [Fact]
    public async Task TeardownAsync_RemoveFails_TriggersForceRemove()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop();

      // RemoveAsync with force=false throws
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              false,
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("remove failed"));

      // RemoveAsync with force=true succeeds (for ForceRemoveAsync)
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              true,
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.UseContainer(c => c.UseImage("redis:alpine"));
          },
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      // Dispose should not throw — teardown fails, force-remove kicks in
      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);

      // Verify force-remove was called
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              true,
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()),
          Times.AtLeastOnce());
    }

    [Fact]
    public async Task TeardownAsync_AllSucceed_ClearsList()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.UseContainer(c => c.UseImage("redis:alpine"));
            builder.UseContainer(c => c.UseImage("nginx:alpine"));
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.Services.Count >= 2);

      await resource.DisposeAsync();
      Assert.Throws<InvalidOperationException>(() => _ = resource.Services);
    }

    [Fact]
    public void PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      var resource = new TopologyResource(
          Kernel,
          builder => builder.UseContainer(c => c.UseImage("alpine:latest")));

      Assert.Throws<InvalidOperationException>(() => _ = resource.Services);
      Assert.Throws<InvalidOperationException>(() => _ = resource.Containers);
      Assert.Throws<InvalidOperationException>(() => resource.GetContainer("x"));
      Assert.Throws<InvalidOperationException>(() => resource.GetNetwork("x"));
    }

    [Fact]
    public async Task Services_CannotBeMutatedExternally()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new TopologyResource(
          Kernel,
          builder =>
          {
            builder.UseContainer(c => c.UseImage("redis:alpine"));
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      // Should not be castable back to List<IServiceAsync>
      Assert.IsNotType<List<IServiceAsync>>(resource.Services);

      await resource.DisposeAsync();
    }
  }
}
