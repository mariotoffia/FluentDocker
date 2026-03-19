using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for network operations via INetworkDriver.
  /// Ported from V2 FluentNetworkTests.cs
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "FluentNetwork")]
  [Collection("DockerDriver")]
  public class FluentNetworkTests : DockerDriverTestBase
  {
    #region Custom Network Tests

    [Fact]
    public async Task Create_NetworkWithSubnet_CanConnectContainer()
    {
      string? containerId = null;
      string? networkId = null;
      var networkName = UniqueName("network");

      try
      {
        // Arrange - Create network with specific subnet
        var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge",
          Subnet = "10.18.0.0/16"
        }, TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success, $"Network create failed: {networkResult.Error}");
        networkId = networkResult.Data.Id;

        // Act - Create container on the network
        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          NetworkMode = networkName,
          Detach = true
        }, TestContext.Current.CancellationToken);

        Assert.True(containerResult.Success, $"Container create failed: {containerResult.Error}");
        containerId = containerResult.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.NetworkSettings?.Networks);
        Assert.True(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName));
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        if (networkId != null)
          await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Create_InternalNetwork_CreatesSuccessfully()
    {
      string? networkId = null;
      var networkName = UniqueName("internal");

      try
      {
        // Act - Create internal network
        var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge",
          Internal = true
        }, TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success, $"Network create failed: {networkResult.Error}");
        networkId = networkResult.Data.Id;

        // Assert
        var inspect = await NetworkDriver.InspectAsync(Context, networkId, TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.True(inspect.Data.Internal);
      }
      finally
      {
        if (networkId != null)
          await RemoveNetworkAsync(networkId);
      }
    }

    #endregion

    #region Container Network Connectivity Tests

    [Fact]
    public async Task Containers_OnSameNetwork_CanCommunicate()
    {
      string? container1Id = null;
      string? container2Id = null;
      string? networkId = null;
      var networkName = UniqueName("shared");

      try
      {
        // Arrange - Create shared network
        var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge"
        }, TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success);
        networkId = networkResult.Data.Id;

        // Start first container
        var container1Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Name = UniqueName("cont1"),
          NetworkMode = networkName,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(container1Result.Success);
        container1Id = container1Result.Data.Id;

        // Start second container
        var container2Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Name = UniqueName("cont2"),
          NetworkMode = networkName,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(container2Result.Success);
        container2Id = container2Result.Data.Id;

        // Act - Get container1's IP and ping from container2
        var inspect1 = await ContainerDriver.InspectAsync(Context, container1Id, TestContext.Current.CancellationToken);
        Assert.True(inspect1.Success);

        var container1Ip = inspect1.Data.NetworkSettings?.Networks?[networkName]?.IPAddress;
        Assert.NotNull(container1Ip);

        // Ping from container2 to container1
        var pingResult = await ContainerDriver.ExecAsync(Context, container2Id, new ExecConfig
        {
          Command = new[] { "ping", "-c", "1", "-W", "3", container1Ip }
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(pingResult.Success);
      }
      finally
      {
        if (container2Id != null)
          await RemoveContainerAsync(container2Id);
        if (container1Id != null)
          await RemoveContainerAsync(container1Id);
        if (networkId != null)
          await RemoveNetworkAsync(networkId);
      }
    }

    #endregion

    #region Connect/Disconnect Tests

    [Fact]
    public async Task Connect_ContainerToNetwork_UpdatesContainerNetworks()
    {
      string? containerId = null;
      string? networkId = null;
      var networkName = UniqueName("connect");

      try
      {
        // Arrange
        networkId = await CreateNetworkAsync(networkName);
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Act
        var connectResult = await NetworkDriver.ConnectAsync(Context, networkId, containerId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(connectResult.Success);

        var inspect = await ContainerDriver.InspectAsync(Context, containerId, TestContext.Current.CancellationToken);
        Assert.True(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName));
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        if (networkId != null)
          await RemoveNetworkAsync(networkId);
      }
    }

    [Fact]
    public async Task Disconnect_ConnectedContainer_RemovesFromContainerNetworks()
    {
      string? containerId = null;
      string? networkId = null;
      var networkName = UniqueName("disconnect");

      try
      {
        // Arrange
        networkId = await CreateNetworkAsync(networkName);

        // Create container on the network
        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          NetworkMode = networkName,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(containerResult.Success);
        containerId = containerResult.Data.Id;

        // Act
        var disconnectResult = await NetworkDriver.DisconnectAsync(Context, networkId, containerId, force: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(disconnectResult.Success);

        var inspect = await ContainerDriver.InspectAsync(Context, containerId, TestContext.Current.CancellationToken);
        // Container should no longer be on the custom network
        Assert.False(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName));
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        if (networkId != null)
          await RemoveNetworkAsync(networkId);
      }
    }

    #endregion

    #region Network Inspection Tests

    [Fact]
    public async Task Inspect_NetworkWithContainers_ReturnsNetworkDetails()
    {
      string? containerId = null;
      string? networkId = null;
      var networkName = UniqueName("inspect");

      try
      {
        // Arrange
        networkId = await CreateNetworkAsync(networkName);

        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          NetworkMode = networkName,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(containerResult.Success);
        containerId = containerResult.Data.Id;

        // Act
        var inspectResult = await NetworkDriver.InspectAsync(Context, networkId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(inspectResult.Success);
        Assert.NotNull(inspectResult.Data);
        Assert.Equal(networkName, inspectResult.Data.Name);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        if (networkId != null)
          await RemoveNetworkAsync(networkId);
      }
    }

    #endregion
  }
}
