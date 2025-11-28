using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.ComposeTests
{
    /// <summary>
    /// Basic Docker Compose tests - ported from V2 FluentDockerComposeTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    [Trait("Category", "Compose")]
    public class ComposeBasicTests : DockerTestBase
    {
        private string _composeFile;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            // Create a simple compose file for testing
            _composeFile = Path.Combine(Path.GetTempPath(), $"docker-compose-{Guid.NewGuid():N}.yml");
            await File.WriteAllTextAsync(_composeFile, @"
version: '3.8'
services:
  web:
    image: nginx:alpine
    ports:
      - ""8080:80""
  redis:
    image: redis:alpine
");
        }

        public override Task DisposeAsync()
        {
            if (File.Exists(_composeFile))
                File.Delete(_composeFile);
            return base.DisposeAsync();
        }

        [Fact]
        public async Task Compose_FromFile_ShouldStartServices()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseCompose(c => c
                    .WithComposeFile(_composeFile)
                    .WithProjectName($"test-{Guid.NewGuid():N}".Substring(0, 15)))
                .BuildAsync();

            var compose = GetCompose(scope);
            Assert.NotNull(compose);

            await compose.StartAsync();

            // Wait a moment for services to start
            await Task.Delay(2000);

            // Assert
            var services = await compose.ListServicesAsync();
            Assert.True(services.IsSuccess);
            Assert.NotEmpty(services.Data);
        }

        [Fact]
        public async Task Compose_Stop_ShouldStopAllServices()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseCompose(c => c
                    .WithComposeFile(_composeFile)
                    .WithProjectName($"test-{Guid.NewGuid():N}".Substring(0, 15)))
                .BuildAsync();

            var compose = GetCompose(scope);
            await compose.StartAsync();
            await Task.Delay(2000);

            // Act
            await compose.StopAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Stopped, compose.State);
        }

        [Fact]
        public async Task Compose_GetLogs_ShouldReturnLogs()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseCompose(c => c
                    .WithComposeFile(_composeFile)
                    .WithProjectName($"test-{Guid.NewGuid():N}".Substring(0, 15)))
                .BuildAsync();

            var compose = GetCompose(scope);
            await compose.StartAsync();
            await Task.Delay(2000);

            // Act
            var logs = await compose.GetLogsAsync();

            // Assert
            Assert.True(logs.IsSuccess);
            // Logs may be empty initially but the call should succeed
        }
    }
}
