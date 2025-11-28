using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.ComposeTests
{
    /// <summary>
    /// Docker Compose pause/resume tests - ported from V2 FluentDockerComposeTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    [Trait("Category", "Compose")]
    public class ComposePauseResumeTests : DockerTestBase
    {
        private string _composeFile;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            // Create a compose file for testing
            _composeFile = Path.Combine(Path.GetTempPath(), $"docker-compose-{Guid.NewGuid():N}.yml");
            await File.WriteAllTextAsync(_composeFile, @"
version: '3.8'
services:
  web:
    image: nginx:alpine
    ports:
      - ""8081:80""
");
        }

        public override Task DisposeAsync()
        {
            if (File.Exists(_composeFile))
                File.Delete(_composeFile);
            return base.DisposeAsync();
        }

        [Fact]
        public async Task Compose_PauseAndResume_ShouldWork()
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

            Assert.Equal(ServiceRunningState.Running, compose.State);

            // Act - Pause
            await compose.PauseAsync();

            // Assert - Paused
            Assert.Equal(ServiceRunningState.Paused, compose.State);

            // Act - Resume
            await compose.StartAsync();

            // Assert - Running again
            Assert.Equal(ServiceRunningState.Running, compose.State);
        }

        [Fact]
        public async Task Compose_Restart_ShouldWork()
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
            Assert.Equal(ServiceRunningState.Stopped, compose.State);

            await compose.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, compose.State);
        }
    }
}
