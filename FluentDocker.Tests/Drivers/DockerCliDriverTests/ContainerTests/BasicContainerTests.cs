using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.ContainerTests
{
    /// <summary>
    /// Basic container operations tests - ported from V2 FluentContainerBasicTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class BasicContainerTests : DockerTestBase
    {
        [Fact]
        public async Task BuildContainer_WithoutStart_ShouldBeInStoppedState()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithCommand("echo", "hello"))
                .BuildAsync();

            // Assert
            var container = GetContainer(scope);
            Assert.NotNull(container);
            Assert.Equal(ServiceRunningState.Stopped, container.State);
        }

        [Fact]
        public async Task StartContainer_WithEnvironmentVariable_ShouldReflectInConfiguration()
        {
            // Arrange & Act
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithEnvironment("TEST_VAR", "test_value")
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, container.State);

            var config = await container.InspectAsync();
            Assert.True(config.IsSuccess);
            Assert.Contains(config.Data.Config.Env, e => e.Contains("TEST_VAR=test_value"));
        }

        [Fact]
        public async Task PauseAndResume_ShouldWorkOnContainer()
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
            Assert.Equal(ServiceRunningState.Running, container.State);

            // Act - Pause
            await container.PauseAsync();

            // Assert - Paused
            Assert.Equal(ServiceRunningState.Paused, container.State);
            var config = await container.InspectAsync();
            Assert.True(config.IsSuccess);
            Assert.Equal("paused", config.Data.State.Status.ToLower());

            // Act - Resume
            await container.StartAsync();

            // Assert - Running
            Assert.Equal(ServiceRunningState.Running, container.State);
            config = await container.InspectAsync();
            Assert.True(config.IsSuccess);
            Assert.Equal("running", config.Data.State.Status.ToLower());
        }

        [Fact]
        public async Task KeepContainer_OnDispose_ShouldLeaveContainerInArchive()
        {
            // Arrange
            string containerId;
            await using (var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithCommand("sleep", "5")
                    .KeepContainer())
                .BuildAsync())
            {
                var container = GetContainer(scope);
                await container.StartAsync();
                containerId = container.Id;
                Assert.NotNull(containerId);
            }

            // Act - Container should still exist after dispose
            var containerDriver = Kernel.SysCtl<FluentDocker.Drivers.IContainerDriver>(DriverId);
            var inspectResult = await containerDriver.InspectAsync(containerId);

            // Assert
            Assert.True(inspectResult.IsSuccess);

            // Cleanup
            await containerDriver.RemoveAsync(containerId, force: true);
        }

        [Fact]
        public async Task StopContainer_ShouldChangeStateToStopped()
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
            Assert.Equal(ServiceRunningState.Running, container.State);

            // Act
            await container.StopAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Stopped, container.State);
        }

        [Fact]
        public async Task Container_WithName_ShouldHaveCorrectName()
        {
            // Arrange
            var containerName = $"test-container-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName(containerName)
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Contains(containerName, config.Data.Name);
        }

        [Fact]
        public async Task Container_WithLabels_ShouldHaveCorrectLabels()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithLabel("test.label", "test-value")
                    .WithLabel("test.label2", "test-value2")
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.True(config.Data.Config.Labels.ContainsKey("test.label"));
            Assert.Equal("test-value", config.Data.Config.Labels["test.label"]);
        }

        [Fact]
        public async Task Container_WithWorkingDirectory_ShouldHaveCorrectWorkDir()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithWorkingDirectory("/tmp")
                    .WithCommand("pwd"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Equal("/tmp", config.Data.Config.WorkingDir);
        }

        [Fact]
        public async Task Container_WithUser_ShouldRunAsSpecifiedUser()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithUser("nobody")
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Equal("nobody", config.Data.Config.User);
        }
    }
}
