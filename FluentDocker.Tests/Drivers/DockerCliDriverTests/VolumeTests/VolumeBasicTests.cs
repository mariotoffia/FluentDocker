using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.VolumeTests
{
    /// <summary>
    /// Basic volume operations tests - ported from V2 FluentVolumeTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class VolumeBasicTests : DockerTestBase
    {
        [Fact]
        public async Task CreateVolume_ShouldSucceed()
        {
            // Arrange & Act
            var volumeName = $"test-vol-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName(volumeName))
                .BuildAsync();

            // Assert
            var volume = GetVolume(scope);
            Assert.NotNull(volume);
            Assert.Equal(volumeName, volume.Name);
        }

        [Fact]
        public async Task CreateVolume_WithLabels_ShouldHaveLabels()
        {
            // Arrange
            var volumeName = $"test-vol-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName(volumeName)
                    .WithLabel("test.label", "test-value"))
                .BuildAsync();

            var volume = GetVolume(scope);

            // Act
            var config = await volume.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.True(config.Data.Labels.ContainsKey("test.label"));
            Assert.Equal("test-value", config.Data.Labels["test.label"]);
        }

        [Fact]
        public async Task Volume_WithoutRemoveOnDispose_ShouldPersistAfterDispose()
        {
            // Arrange
            var volumeName = $"test-persist-{Guid.NewGuid():N}".Substring(0, 20);

            await using (var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName(volumeName))
                .BuildAsync())
            {
                // Volume created
            }

            // Act - Check if volume still exists
            var volumeDriver = Kernel.SysCtl<FluentDocker.Drivers.IVolumeDriver>(DriverId);
            var inspectResult = await volumeDriver.InspectAsync(volumeName);

            // Assert
            Assert.True(inspectResult.IsSuccess);

            // Cleanup
            await volumeDriver.RemoveAsync(volumeName, force: true);
        }

        [Fact]
        public async Task Volume_MountedToContainer_ShouldBeAccessible()
        {
            // Arrange
            var volumeName = $"test-mount-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName(volumeName))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithVolume(volumeName, "/data")
                    .WithCommand("sleep", "30"))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var config = await container.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Contains(config.Data.Mounts, m => m.Name == volumeName && m.Destination == "/data");
        }

        [Fact]
        public async Task Volume_WithDriver_ShouldUseSpecifiedDriver()
        {
            // Arrange
            var volumeName = $"test-driver-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName(volumeName)
                    .UseDriver("local"))
                .BuildAsync();

            var volume = GetVolume(scope);

            // Act
            var config = await volume.InspectAsync();

            // Assert
            Assert.True(config.IsSuccess);
            Assert.Equal("local", config.Data.Driver);
        }

        [Fact]
        public async Task TwoContainers_SharingVolume_ShouldSeeEachOthersData()
        {
            // Arrange
            var volumeName = $"test-shared-{Guid.NewGuid():N}".Substring(0, 20);

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName(volumeName))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("writer")
                    .WithVolume(volumeName, "/data")
                    .WithCommand("sh", "-c", "echo 'hello' > /data/test.txt && sleep 30"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("reader")
                    .WithVolume(volumeName, "/data")
                    .WithCommand("sleep", "60"))
                .BuildAsync();

            var writer = GetContainer(scope, n => n.Contains("writer"));
            var reader = GetContainer(scope, n => n.Contains("reader"));

            await writer.StartAsync();
            await Task.Delay(1000); // Give time for file to be written
            await reader.StartAsync();

            // Act - Read the file from the reader container
            var execResult = await reader.ExecuteAsync(new[] { "cat", "/data/test.txt" });

            // Assert
            Assert.True(execResult.IsSuccess);
            Assert.Contains("hello", execResult.Data);
        }
    }
}
