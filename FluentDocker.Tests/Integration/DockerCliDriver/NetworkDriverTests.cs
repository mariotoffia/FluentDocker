using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for INetworkDriver operations.
  /// Requires Docker daemon to be running.
  /// </summary>
  [Trait("Category", "Integration")]
  [Collection("DockerDriver")]
  public class NetworkDriverTests : DockerDriverTestBase
  {
    [Fact]
    public async Task List_ReturnsDefaultNetworks()
    {
      // Act
      var result = await NetworkDriver.ListAsync(Context, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result.Success);
      Assert.True(result.Data.Count > 0, "Should have at least default networks");
      Assert.Contains(result.Data, n => n.Name == "bridge");
      Assert.Contains(result.Data, n => n.Name == "host");
    }

    [Fact]
    public async Task Inspect_BridgeNetwork_ReturnsDetails()
    {
      // Arrange
      var networks = await NetworkDriver.ListAsync(Context, cancellationToken: TestContext.Current.CancellationToken);
      var bridge = networks.Data.First(n => n.Name == "bridge");

      // Act
      var result = await NetworkDriver.InspectAsync(Context, bridge.Id, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result.Success);
      Assert.NotNull(result.Data);
      Assert.Equal("bridge", result.Data.Name);
    }

    [Fact]
    public async Task Create_WithName_CreatesNetwork()
    {
      string? networkId = null;
      var networkName = UniqueName("network");
      try
      {
        // Act
        var result = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge"
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Create failed: {result.Error}");
        networkId = result.Data.Id;
        Assert.NotNull(networkId);

        // Verify network exists
        var inspect = await NetworkDriver.InspectAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.Equal(networkName, inspect.Data.Name);
      }
      finally
      {
        await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Remove_ExistingNetwork_RemovesSuccessfully()
    {
      // Arrange
      var networkName = UniqueName("network");
      var createResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
      {
        Name = networkName
      }, cancellationToken: TestContext.Current.CancellationToken);
      var networkId = createResult.Data.Id;

      // Act
      var result = await NetworkDriver.RemoveAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result.Success);

      // Verify network is gone
      var inspect = await NetworkDriver.InspectAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(inspect.Success);
    }

    [Fact]
    public async Task Create_WithSubnet_CreatesNetworkWithSubnet()
    {
      string? networkId = null;
      var networkName = UniqueName("network");
      try
      {
        // Act
        var result = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge",
          Subnet = "10.20.0.0/16"
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Create failed: {result.Error}");
        networkId = result.Data.Id;

        var inspect = await NetworkDriver.InspectAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
      }
      finally
      {
        await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Connect_ContainerToNetwork_ConnectsSuccessfully()
    {
      string? containerId = null;
      string? networkId = null;
      try
      {
        // Arrange
        var networkName = UniqueName("network");
        networkId = await CreateNetworkAsync(networkName);
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await NetworkDriver.ConnectAsync(Context, networkId, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Connect failed: {result.Error}");

        // Verify container is connected
        var inspect = await NetworkDriver.InspectAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
        await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Disconnect_ConnectedContainer_DisconnectsSuccessfully()
    {
      string? containerId = null;
      string? networkId = null;
      try
      {
        // Arrange
        var networkName = UniqueName("network");
        networkId = await CreateNetworkAsync(networkName);
        containerId = await RunContainerAsync(NginxImage);
        await NetworkDriver.ConnectAsync(Context, networkId, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await NetworkDriver.DisconnectAsync(Context, networkId, containerId, force: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Disconnect failed: {result.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
        await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Create_ContainerWithStaticIp_UsesStaticIp()
    {
      string? containerId = null;
      string? networkId = null;
      var networkName = UniqueName("network");
      var staticIp = "10.18.0.22";
      try
      {
        // Arrange - create network with specific subnet
        var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge",
          Subnet = "10.18.0.0/16"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success, $"Network create failed: {networkResult.Error}");
        networkId = networkResult.Data.Id;

        // Create container with network AND the static IP
        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          NetworkMode = networkName,
          Ipv4Address = staticIp,
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(containerResult.Success, $"Run failed: {containerResult.Error}");
        containerId = containerResult.Data.Id;

        // Assert — verify the static IP was actually assigned
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.NetworkSettings?.Networks);
        Assert.True(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName),
            $"Expected network '{networkName}' in container networks; found: " +
            string.Join(", ", inspect.Data.NetworkSettings.Networks.Keys));
        var netEndpoint = inspect.Data.NetworkSettings.Networks[networkName];
        Assert.Equal(staticIp, netEndpoint.IPAddress);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
        await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Create_InternalNetwork_CreatesInternalNetwork()
    {
      string? networkId = null;
      var networkName = UniqueName("internal");
      try
      {
        // Act
        var result = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge",
          Internal = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Create failed: {result.Error}");
        networkId = result.Data.Id;

        var inspect = await NetworkDriver.InspectAsync(Context, networkId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.True(inspect.Data.Internal);
      }
      finally
      {
        await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task List_WithNameFilter_FiltersResults()
    {
      string? networkId = null;
      var networkName = UniqueName("filtertest");
      try
      {
        // Arrange
        networkId = await CreateNetworkAsync(networkName);

        // Act
        var result = await NetworkDriver.ListAsync(Context, new NetworkListFilter
        {
          Name = networkName
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(result.Data, n => n.Name == networkName);
      }
      finally
      {
        await RemoveNetworkAsync(networkId);
      }
    }
  }
}

