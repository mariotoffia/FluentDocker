using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.NetworkTests
{
    /// <summary>
    /// Network with container tests - ported from V2 FluentNetworkTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class NetworkContainerTests : DockerTestBase
    {
        [Fact]
        public async Task Container_WithStaticIp_InCustomNetwork_ShouldWork()
        {
            // Arrange
            var networkName = $"test-staticip-{Guid.NewGuid():N}".Substring(0, 20);
            const string expectedIp = "10.19.0.22";

            await using var networkScope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName(networkName)
                    .UseDriver("bridge")
                    .WithSubnet("10.19.0.0/16"))
                .BuildAsync();

            var network = GetNetwork(networkScope);

            await using var containerScope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithNetwork(networkName)
                    .WithCommand("sleep", "60"))
                .BuildAsync();

            var container = GetContainer(containerScope);

            // Connect with static IP
            var networkDriver = Kernel.SysCtl<FluentDocker.Drivers.INetworkDriver>(DriverId);
            await networkDriver.ConnectAsync(network.Id, container.Id, new FluentDocker.Drivers.NetworkConnectConfig
            {
                IPv4Address = expectedIp
            });

            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            var networkSettings = config.Data.NetworkSettings.Networks[networkName];
            Assert.Equal(expectedIp, networkSettings.IPAddress);
        }

        [Fact]
        public async Task Container_InInternalNetwork_ShouldNotReachExternal()
        {
            // Arrange
            var networkName = $"test-internal-{Guid.NewGuid():N}".Substring(0, 18);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName(networkName)
                    .UseDriver("bridge")
                    .AsInternal())
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithNetwork(networkName)
                    .WithCommand("ping", "-c", "1", "-W", "2", "1.1.1.1"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Wait for container to finish
            await Task.Delay(3000);

            // Act
            var config = await container.InspectAsync();

            // Assert - Container should have exited with error (ping failed)
            Assert.True(config.IsSuccess);
            // Exit code 1 indicates ping failure (no route to host in internal network)
            Assert.NotEqual(0, config.Data.State.ExitCode);
        }

        [Fact]
        public async Task TwoContainers_InSameNetwork_ShouldCommunicate()
        {
            // Arrange
            var networkName = $"test-comm-{Guid.NewGuid():N}".Substring(0, 18);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName(networkName)
                    .UseDriver("bridge"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("container1")
                    .WithNetwork(networkName)
                    .WithCommand("sleep", "60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("container2")
                    .WithNetwork(networkName)
                    .WithCommand("sleep", "60"))
                .BuildAsync();

            var container1 = GetContainer(scope, n => n.Contains("container1"));
            var container2 = GetContainer(scope, n => n.Contains("container2"));

            await container1.StartAsync();
            await container2.StartAsync();

            // Act - Ping container1 from container2
            var execResult = await container2.ExecuteAsync(new[] { "ping", "-c", "1", "container1" });

            // Assert
            Assert.True(execResult.IsSuccess);
        }
    }
}
