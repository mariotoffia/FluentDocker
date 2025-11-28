using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.ContainerTests
{
    /// <summary>
    /// Port mapping tests - ported from V2 FluentContainerBasicTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class PortMappingTests : DockerTestBase
    {
        [Fact]
        public async Task ExplicitPortMapping_ShouldMapToSpecificHostPort()
        {
            // Arrange
            const int hostPort = 45432;
            const int containerPort = 5432;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("postgres:15-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", "mysecretpassword")
                    .WithPort($"{containerPort}", $"{hostPort}")
                    .WaitForPort($"{containerPort}/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var endpoint = await container.ToHostExposedEndpointAsync($"{containerPort}/tcp");

            // Assert
            Assert.NotNull(endpoint);
            Assert.Equal(hostPort, endpoint.Port);
        }

        [Fact]
        public async Task ImplicitPortMapping_ShouldMapToRandomHostPort()
        {
            // Arrange
            const int containerPort = 5432;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("postgres:15-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", "mysecretpassword")
                    .ExposePort($"{containerPort}")
                    .WaitForPort($"{containerPort}/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var endpoint = await container.ToHostExposedEndpointAsync($"{containerPort}/tcp");

            // Assert
            Assert.NotNull(endpoint);
            Assert.NotEqual(0, endpoint.Port);
        }

        [Fact]
        public async Task MultiplePortMappings_ShouldWorkCorrectly()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .WithPort("80", "48080")
                    .WithPort("443", "48443"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var endpoint80 = await container.ToHostExposedEndpointAsync("80/tcp");
            var endpoint443 = await container.ToHostExposedEndpointAsync("443/tcp");

            // Assert
            Assert.NotNull(endpoint80);
            Assert.NotNull(endpoint443);
            Assert.Equal(48080, endpoint80.Port);
            Assert.Equal(48443, endpoint443.Port);
        }

        [Fact]
        public async Task UdpPortMapping_ShouldWork()
        {
            // Arrange - Using a simple image that can handle UDP
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithPort("53/udp", "45353")
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            // Verify the port binding exists
            Assert.NotNull(config.Data.NetworkSettings.Ports);
        }
    }
}
