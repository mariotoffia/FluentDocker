using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.RegressionTests
{
    /// <summary>
    /// Regression tests for specific issues - ported from V2 IssuesTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    [Trait("Category", "Regression")]
    public class IssueTests : DockerTestBase
    {
        /// <summary>
        /// Issue 92: WaitForPort with specific address
        /// </summary>
        [Fact]
        public async Task Issue92_WaitForPort_WithSpecificAddress_ShouldWork()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .WithName($"test-issue92-{Guid.NewGuid():N}".Substring(0, 20))
                    .ExposePort("80")
                    .WaitForPort("80/tcp", "127.0.0.1", 30000))
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
            var response = await httpClient.GetAsync($"http://127.0.0.1:{endpoint.Port}/");
            Assert.True(response.IsSuccessStatusCode);
        }

        /// <summary>
        /// Test that ReuseIfExists works correctly with named containers
        /// </summary>
        [Fact]
        public async Task ReuseIfExists_WithNamedContainer_ShouldReuseExisting()
        {
            // Arrange
            var containerName = $"reuse-test-{Guid.NewGuid():N}".Substring(0, 20);

            // Create first container
            await using var scope1 = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName(containerName)
                    .WithCommand("sleep", "60")
                    .ReuseIfExists()
                    .KeepContainer())
                .BuildAsync();

            var container1 = GetContainer(scope1);
            await container1.StartAsync();
            var id1 = container1.Id;

            // Act - Create second container with same name and ReuseIfExists
            await using var scope2 = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName(containerName)
                    .WithCommand("sleep", "60")
                    .ReuseIfExists())
                .BuildAsync();

            var container2 = GetContainer(scope2);
            await container2.StartAsync();
            var id2 = container2.Id;

            // Assert - Should be the same container
            Assert.Equal(id1, id2);
        }

        /// <summary>
        /// Test that DestroyIfExists removes existing container
        /// </summary>
        [Fact]
        public async Task DestroyIfExists_WithExistingContainer_ShouldRemoveAndRecreate()
        {
            // Arrange
            var containerName = $"destroy-test-{Guid.NewGuid():N}".Substring(0, 20);

            // Create first container
            await using var scope1 = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName(containerName)
                    .WithCommand("sleep", "60")
                    .KeepContainer())
                .BuildAsync();

            var container1 = GetContainer(scope1);
            await container1.StartAsync();
            var id1 = container1.Id;

            // Act - Create second container with same name and DestroyIfExists
            await using var scope2 = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName(containerName)
                    .WithCommand("sleep", "60")
                    .DestroyIfExists())
                .BuildAsync();

            var container2 = GetContainer(scope2);
            await container2.StartAsync();
            var id2 = container2.Id;

            // Assert - Should be different containers
            Assert.NotEqual(id1, id2);
        }

        /// <summary>
        /// Test that environment variables with special characters work
        /// </summary>
        [Fact]
        public async Task EnvironmentVariable_WithSpecialCharacters_ShouldWork()
        {
            // Arrange
            const string specialValue = "pa$$word=with!@#$%^&*()";

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithEnvironment("SPECIAL_VAR", specialValue)
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Contains(config.Data.Config.Env, e => e.Contains("SPECIAL_VAR="));
        }

        /// <summary>
        /// Test that container logs can be retrieved
        /// </summary>
        [Fact]
        public async Task GetLogs_AfterContainerRuns_ShouldReturnOutput()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithCommand("echo", "hello from container"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Wait for container to complete
            await Task.Delay(1000);

            // Act
            var logs = await container.GetLogsAsync();

            // Assert
            Assert.True(logs.IsSuccess);
            Assert.Contains("hello from container", logs.Data);
        }

        /// <summary>
        /// Test that exec in container works
        /// </summary>
        [Fact]
        public async Task Execute_InRunningContainer_ShouldReturnOutput()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithCommand("sleep", "60"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var result = await container.ExecuteAsync(new[] { "echo", "test output" });

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Contains("test output", result.Data);
        }
    }
}
