using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
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

    [Fact]
    public async Task DisposeAsync_GracefulRemoveFails_PropagatesInsteadOfSwallowing()
    {
      // The CLI/API network driver returns CommandResponse.Fail (it does not throw)
      // when a network can't be removed. TeardownAsync must surface that instead of
      // reporting a clean teardown — otherwise the resource leaks silently.
      MockPack.SetupNetworkCreate("net-stuck");
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              "network has active endpoints", ErrorCodes.Network.RemoveFailed));

      var resource = new NetworkResource(
          Kernel,
          config => config.Name = "stuck-network",
          new DockerResourceOptions { ForceRemoveOnDispose = false });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<DriverException>(
          () => resource.DisposeAsync().AsTask());
    }
  }
}
