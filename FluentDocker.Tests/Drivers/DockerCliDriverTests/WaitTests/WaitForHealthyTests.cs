using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.WaitTests
{
    /// <summary>
    /// Wait for healthy container tests - tests for Docker healthcheck
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class WaitForHealthyTests : DockerTestBase
    {
        [Fact]
        public async Task WaitForHealthy_WithPostgres_ShouldWaitForHealthCheck()
        {
            // Arrange - Postgres has a built-in healthcheck in recent versions
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("postgres:15-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", "mysecretpassword")
                    .ExposePort("5432")
                    .WaitForHealthy(60000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Equal(ServiceRunningState.Running, container.State);
        }

        [Fact]
        public async Task WaitForProcess_WithSleepCommand_ShouldDetectProcess()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithCommand("sleep", "60")
                    .WaitForProcess("sleep", 10000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, container.State);
        }
    }
}
