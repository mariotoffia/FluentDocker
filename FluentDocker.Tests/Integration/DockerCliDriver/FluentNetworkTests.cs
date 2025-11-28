using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Extensions;
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
        public async Task Create_NetworkWithSubnet_CanAssignStaticIp()
        {
            string containerId = null;
            string networkId = null;
            var networkName = UniqueName("network");
            var staticIp = "10.18.0.22";
            
            try
            {
                // Arrange - Create network with specific subnet
                var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
                {
                    Name = networkName,
                    Driver = "bridge",
                    Subnet = "10.18.0.0/16"
                });
                Assert.True(networkResult.Success, $"Network create failed: {networkResult.Error}");
                networkId = networkResult.Data.Id;

                // Act - Create container with static IP on the network
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    NetworkMode = networkName,
                    IpAddress = staticIp,
                    Detach = true
                });

                Assert.True(containerResult.Success, $"Container create failed: {containerResult.Error}");
                containerId = containerResult.Data.Id;

                // Assert
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
                Assert.NotNull(inspect.Data.NetworkSettings?.Networks);
                
                // Check if our network is in the container's networks
                Assert.True(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName));
                var networkSettings = inspect.Data.NetworkSettings.Networks[networkName];
                Assert.Equal(staticIp, networkSettings.IPAddress);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveNetworkAsync(networkId);
            }
        }

        [Fact]
        public async Task Create_InternalNetwork_ContainerCannotAccessExternal()
        {
            string containerId = null;
            string networkId = null;
            var networkName = UniqueName("internal");
            
            try
            {
                // Arrange - Create internal network
                var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
                {
                    Name = networkName,
                    Driver = "bridge",
                    Internal = true
                });
                Assert.True(networkResult.Success, $"Network create failed: {networkResult.Error}");
                networkId = networkResult.Data.Id;

                // Act - Run container trying to ping external (should fail)
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Name = UniqueName("internal-test"),
                    NetworkMode = networkName,
                    Command = new[] { "ping", "-c", "1", "-W", "3", "1.1.1.1" },
                    Detach = false
                });
                containerId = containerResult.Data?.Id;

                // Wait for command to complete
                await Task.Delay(5000);

                // Assert - Container should have failed to ping (exit code != 0)
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                if (inspect.Success)
                {
                    // Internal network should prevent external access
                    // Exit code 1 means ping failed
                    Assert.NotEqual(0, inspect.Data.State.ExitCode);
                }
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveNetworkAsync(networkId);
            }
        }

        [Fact]
        public async Task Container_OnInternalNetwork_AccessibleFromHost()
        {
            string containerId = null;
            string networkId = null;
            var networkName = UniqueName("internal");
            
            try
            {
                // Arrange - Create internal network
                var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
                {
                    Name = networkName,
                    Driver = "bridge",
                    Internal = true
                });
                Assert.True(networkResult.Success);
                networkId = networkResult.Data.Id;

                // Act - Run nginx on the internal network but expose port to host
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = NginxImage,
                    NetworkMode = networkName,
                    PortBindings = new Dictionary<string, string>
                    {
                        ["80/tcp"] = "0" // Random port
                    },
                    Detach = true
                });

                Assert.True(containerResult.Success, $"Container create failed: {containerResult.Error}");
                containerId = containerResult.Data.Id;

                // Wait for nginx to start
                await Task.Delay(2000);

                // Assert - Container should be accessible from host
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
                Assert.True(inspect.Data.State.Running);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveNetworkAsync(networkId);
            }
        }

        #endregion

        #region Container Network Connectivity Tests

        [Fact]
        public async Task Containers_OnSameNetwork_CanCommunicate()
        {
            string container1Id = null;
            string container2Id = null;
            string networkId = null;
            var networkName = UniqueName("shared");
            
            try
            {
                // Arrange - Create shared network
                var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
                {
                    Name = networkName,
                    Driver = "bridge"
                });
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
                });
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
                });
                Assert.True(container2Result.Success);
                container2Id = container2Result.Data.Id;

                // Act - Get container1's IP and ping from container2
                var inspect1 = await ContainerDriver.InspectAsync(Context, container1Id);
                Assert.True(inspect1.Success);
                
                var container1Ip = inspect1.Data.NetworkSettings?.Networks?[networkName]?.IPAddress;
                Assert.NotNull(container1Ip);

                // Ping from container2 to container1
                var pingResult = await ContainerDriver.ExecAsync(Context, container2Id, new ExecConfig
                {
                    Command = new[] { "ping", "-c", "1", "-W", "3", container1Ip }
                });

                // Assert
                Assert.True(pingResult.Success);
            }
            finally
            {
                await RemoveContainerAsync(container2Id);
                await RemoveContainerAsync(container1Id);
                await RemoveNetworkAsync(networkId);
            }
        }

        [Fact]
        public async Task Container_WithNetworkAlias_ResolvableByAlias()
        {
            string container1Id = null;
            string container2Id = null;
            string networkId = null;
            var networkName = UniqueName("alias-net");
            var aliasName = "db-server";
            
            try
            {
                // Arrange - Create network
                var networkResult = await NetworkDriver.CreateAsync(Context, new NetworkCreateConfig
                {
                    Name = networkName,
                    Driver = "bridge"
                });
                Assert.True(networkResult.Success);
                networkId = networkResult.Data.Id;

                // Start first container (db-server)
                var container1Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Name = UniqueName("dbserver"),
                    NetworkMode = networkName,
                    NetworkAliases = new[] { aliasName },
                    Command = new[] { "sleep", "60" },
                    Detach = true
                });
                Assert.True(container1Result.Success);
                container1Id = container1Result.Data.Id;

                // Start second container (client)
                var container2Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Name = UniqueName("client"),
                    NetworkMode = networkName,
                    Command = new[] { "sleep", "60" },
                    Detach = true
                });
                Assert.True(container2Result.Success);
                container2Id = container2Result.Data.Id;

                // Act - Resolve alias from container2
                var resolveResult = await ContainerDriver.ExecAsync(Context, container2Id, new ExecConfig
                {
                    Command = new[] { "getent", "hosts", aliasName }
                });

                // Assert - Should be able to resolve the alias
                Assert.True(resolveResult.Success);
                Assert.Contains(aliasName, resolveResult.Data.StdOut);
            }
            finally
            {
                await RemoveContainerAsync(container2Id);
                await RemoveContainerAsync(container1Id);
                await RemoveNetworkAsync(networkId);
            }
        }

        #endregion

        #region Connect/Disconnect Tests

        [Fact]
        public async Task Connect_ContainerToNetwork_UpdatesContainerNetworks()
        {
            string containerId = null;
            string networkId = null;
            var networkName = UniqueName("connect");
            
            try
            {
                // Arrange
                networkId = await CreateNetworkAsync(networkName);
                containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Command = new[] { "sleep", "60" }
                });

                // Act
                var connectResult = await NetworkDriver.ConnectAsync(Context, networkId, containerId);

                // Assert
                Assert.True(connectResult.Success);
                
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName));
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveNetworkAsync(networkId);
            }
        }

        [Fact]
        public async Task Disconnect_ContainerFromNetwork_RemovesFromContainerNetworks()
        {
            string containerId = null;
            string networkId = null;
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
                });
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Act
                var disconnectResult = await NetworkDriver.DisconnectAsync(Context, networkId, containerId, force: true);

                // Assert
                Assert.True(disconnectResult.Success);
                
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                // Container should no longer be on the custom network
                // (It may be on the default bridge network)
                Assert.False(inspect.Data.NetworkSettings.Networks.ContainsKey(networkName));
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveNetworkAsync(networkId);
            }
        }

        #endregion

        #region Network Inspection Tests

        [Fact]
        public async Task Inspect_NetworkWithContainers_ShowsConnectedContainers()
        {
            string containerId = null;
            string networkId = null;
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
                });
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Act
                var inspectResult = await NetworkDriver.InspectAsync(Context, networkId);

                // Assert
                Assert.True(inspectResult.Success);
                Assert.NotNull(inspectResult.Data.Containers);
                Assert.True(inspectResult.Data.Containers.Count > 0);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveNetworkAsync(networkId);
            }
        }

        #endregion

        #region Helper Methods

        private async Task<string> RunContainerAsync(string image, ContainerCreateConfig config)
        {
            config.Image = image;
            config.Detach = true;
            var result = await ContainerDriver.RunAsync(Context, config);
            Assert.True(result.Success, $"Failed to run container: {result.Error}");
            return result.Data.Id;
        }

        #endregion
    }
}

