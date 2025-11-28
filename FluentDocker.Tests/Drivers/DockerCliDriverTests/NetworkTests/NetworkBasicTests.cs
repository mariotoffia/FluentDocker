using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.NetworkTests
{
    /// <summary>
    /// Basic network operations tests - ported from V2 FluentNetworkTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class NetworkBasicTests : DockerTestBase
    {
        [Fact]
        public async Task CreateNetwork_WithBridgeDriver_ShouldSucceed()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName($"test-network-{Guid.NewGuid():N}".Substring(0, 20))
                    .UseDriver("bridge"))
                .BuildAsync();

            // Assert
            var network = GetNetwork(scope);
            Assert.NotNull(network);
            Assert.NotNull(network.Id);
        }

        [Fact]
        public async Task CreateNetwork_WithSubnet_ShouldSucceed()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName($"test-subnet-{Guid.NewGuid():N}".Substring(0, 20))
                    .UseDriver("bridge")
                    .WithSubnet("10.20.0.0/16"))
                .BuildAsync();

            // Assert
            var network = GetNetwork(scope);
            Assert.NotNull(network);

            var config = await network.InspectAsync();
            Assert.True(config.IsSuccess);
            Assert.Contains(config.Data.IPAM.Config, c => c.Subnet == "10.20.0.0/16");
        }

        [Fact]
        public async Task CreateNetwork_AsInternal_ShouldNotHaveExternalAccess()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName($"test-internal-{Guid.NewGuid():N}".Substring(0, 20))
                    .UseDriver("bridge")
                    .AsInternal())
                .BuildAsync();

            // Assert
            var network = GetNetwork(scope);
            Assert.NotNull(network);

            var config = await network.InspectAsync();
            Assert.True(config.IsSuccess);
            Assert.True(config.Data.Internal);
        }

        [Fact]
        public async Task Network_ConnectAndDisconnect_ShouldWork()
        {
            // Arrange
            var networkName = $"test-connect-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName(networkName)
                    .UseDriver("bridge"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithCommand("sleep", "60"))
                .BuildAsync();

            var network = GetNetwork(scope);
            var container = GetContainer(scope);
            await container.StartAsync();

            // Act - Connect
            var connectResult = await network.ConnectAsync(container.Id);
            Assert.True(connectResult.IsSuccess);

            // Verify connected
            var containerConfig = await container.InspectAsync();
            Assert.True(containerConfig.IsSuccess);
            Assert.Contains(networkName, containerConfig.Data.NetworkSettings.Networks.Keys);

            // Act - Disconnect
            var disconnectResult = await network.DisconnectAsync(container.Id);
            Assert.True(disconnectResult.IsSuccess);

            // Verify disconnected
            containerConfig = await container.InspectAsync();
            Assert.True(containerConfig.IsSuccess);
            Assert.DoesNotContain(networkName, containerConfig.Data.NetworkSettings.Networks.Keys);
        }
    }
}
