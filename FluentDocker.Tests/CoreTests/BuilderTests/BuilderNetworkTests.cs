using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

using NetworkCreateConfig = FluentDocker.Drivers.NetworkCreateConfig;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the v3 Builder with network operations.
  /// Uses mock drivers to test without Docker daemon.
  /// </summary>
  [Trait("Category", "Unit")]
  public class BuilderNetworkTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task UseNetwork_WithName_CreatesNetwork()
    {
      // Arrange
      MockPack
          .SetupNetworkList() // Empty list - network doesn't exist
          .SetupNetworkCreate("network-123")
          .SetupNetworkRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("test-network"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.NotNull(results);
      Assert.Single(results.All);
      var network = results.All.First() as INetworkService;
      Assert.NotNull(network);
      Assert.Equal("test-network", network.NetworkName);

      // Verify create was called
      MockPack.VerifyNetworkCreated("test-network", Times.Once());
    }

    [Fact]
    public async Task UseNetwork_WithDriver_PassesDriver()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("test-network")
              .UseDriver("overlay"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<NetworkCreateConfig>(cfg => cfg.Driver == "overlay"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseNetwork_WithSubnetAndGateway_PassesNetworkConfig()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("test-network")
              .WithSubnet("172.20.0.0/16")
              .WithGateway("172.20.0.1"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<NetworkCreateConfig>(cfg =>
              cfg.Subnet == "172.20.0.0/16" &&
              cfg.Gateway == "172.20.0.1"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseNetwork_WithLabels_PassesLabels()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("test-network")
              .WithLabel("env", "test")
              .WithLabel("project", "myapp"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<NetworkCreateConfig>(cfg =>
              cfg.Labels != null &&
              cfg.Labels["env"] == "test" &&
              cfg.Labels["project"] == "myapp"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseNetwork_AsInternal_PassesInternalFlag()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("internal-network")
              .AsInternal())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<NetworkCreateConfig>(cfg => cfg.Internal),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseNetwork_WithIPv6_PassesIPv6Flag()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("ipv6-network")
              .WithIPv6())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<NetworkCreateConfig>(cfg => cfg.EnableIPv6),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseNetwork_NetworkExists_ReusesExisting()
    {
      // Arrange - Network already exists
      MockPack.SetupNetworkList(new Network
      {
        Id = "existing-network-id",
        Name = "test-network",
        Driver = "bridge"
      });

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("test-network"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - Should not call Create since network exists
      Assert.Single(results.All);
      var network = results.All.First() as INetworkService;
      Assert.NotNull(network);
      Assert.Equal("existing-network-id", network.Id);

      // Create should NOT be called
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<NetworkCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseNetwork_RemoveOnDispose_RemovesOnDispose()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate("network-to-remove")
          .SetupNetworkRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("temp-network")
              .RemoveOnDispose())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      await results.DisposeAllAsync();

      // Assert - Remove should be called
      MockPack.NetworkDriver.Verify(d => d.RemoveAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<string>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseNetwork_MultipleNetworks_CreatesAll()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n.WithName("network-1"))
          .UseNetwork(n => n.WithName("network-2"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal(2, results.All.Count());
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<NetworkCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseNetwork_WithOption_PassesOptions()
    {
      // Arrange
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate()
          .SetupNetworkRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n
              .WithName("custom-network")
              .WithOption("com.docker.network.bridge.name", "my-bridge"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.NetworkDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<NetworkCreateConfig>(cfg =>
              cfg.Options != null &&
              cfg.Options.ContainsKey("com.docker.network.bridge.name")),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }
  }
}
