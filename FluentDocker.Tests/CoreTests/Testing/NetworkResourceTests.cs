using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class NetworkResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesNetwork()
    {
      MockPack
          .SetupNetworkCreate("net-abc123")
          .SetupNetworkRemove();

      var resource = new NetworkResource(
          Kernel,
          config => config.Name = "test-network");

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Equal("net-abc123", resource.NetworkId);
      Assert.Equal("test-network", resource.NetworkName);
    }

    [Fact]
    public async Task DisposeAsync_RemovesNetwork()
    {
      MockPack
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      var resource = new NetworkResource(
          Kernel,
          config => config.Name = "test-network");

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomConfig_PassesConfig()
    {
      MockPack
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      var resource = new NetworkResource(
          Kernel,
          config =>
          {
            config.Name = "custom-net";
            config.Driver = "overlay";
            config.Subnet = "172.28.0.0/16";
            config.EnableIPv6 = true;
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_NoName_GeneratesUniqueName()
    {
      MockPack
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      var resource = new NetworkResource(
          Kernel,
          config => { /* no name set */ });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);
      Assert.StartsWith("net-", resource.NetworkName);
    }

    [Fact]
    public void Constructor_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new NetworkResource(Kernel, null!));
    }
  }
}
