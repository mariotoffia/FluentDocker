using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.WaitTests
{
    /// <summary>
    /// Wait for port tests - ported from V2 FluentContainerBasicTests.cs and WaitTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class WaitForPortTests : DockerTestBase
    {
        [Fact]
        public async Task WaitForPort_WithPostgres_ShouldWaitUntilReady()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("postgres:15-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", "mysecretpassword")
                    .ExposePort("5432")
                    .WaitForPort("5432/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, container.State);
            var endpoint = await container.ToHostExposedEndpointAsync("5432/tcp");
            Assert.NotNull(endpoint);
            Assert.NotEqual(0, endpoint.Port);
        }

        [Fact]
        public async Task WaitForPort_WithNginx_ShouldBeAccessible()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .WaitForPort("80/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");

            // Assert
            Assert.NotNull(endpoint);
            Assert.NotEqual(0, endpoint.Port);

            // Verify HTTP is responding
            using var httpClient = new System.Net.Http.HttpClient();
            var response = await httpClient.GetAsync($"http://localhost:{endpoint.Port}/");
            Assert.True(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task WaitForPort_WithExplicitHostPort_ShouldWork()
        {
            // Arrange
            const int hostPort = 48888;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .WithPort("80", $"{hostPort}")
                    .WaitForPort("80/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");

            // Assert
            Assert.NotNull(endpoint);
            Assert.Equal(hostPort, endpoint.Port);
        }

        [Fact]
        public async Task WaitForPort_WithMultiplePorts_ShouldWaitForAll()
        {
            // Arrange - Redis exposes port 6379
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("redis:alpine")
                    .ExposePort("6379")
                    .WaitForPort("6379/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, container.State);
            var endpoint = await container.ToHostExposedEndpointAsync("6379/tcp");
            Assert.NotNull(endpoint);
        }
    }
}
