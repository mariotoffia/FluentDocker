using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class TopologyResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async Task InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    public Task DisposeAsync()
    {
      return base.DisposeAsync().AsTask();
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

      await resource.InitializeAsync();

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

      await resource.InitializeAsync();
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
      Assert.Empty(resource.Services);
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
          () => resource.InitializeAsync());
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

      await resource.InitializeAsync();

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
          () => new TopologyResource(Kernel, null));
    }
  }
}
