using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class VolumeDriverTests : IDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly IVolumeDriver _driver;
        private readonly DriverContext _context;

        public VolumeDriverTests()
        {
            _kernel = new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<IVolumeDriver>("docker-local");
            _context = new DriverContext("docker-local");
        }

        public void Dispose()
        {
            _kernel?.Dispose();
        }

        [Fact]
        public async Task Volume_Create_CreatesVolume()
        {
            // Arrange
            var volumeName = $"test-volume-{Guid.NewGuid():N}";
            var config = new VolumeCreateConfig
            {
                Name = volumeName
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.Equal(volumeName, response.Data.Name);

            // Cleanup
            await _driver.RemoveAsync(_context, volumeName, force: false);
        }

        [Fact]
        public async Task Volume_List_ReturnsVolumes()
        {
            // Act
            var response = await _driver.ListAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            // May be empty if no volumes exist
        }

        [Fact]
        public async Task Volume_Inspect_ReturnsDetails()
        {
            // Arrange
            var volumeName = $"test-inspect-{Guid.NewGuid():N}";
            var createResponse = await _driver.CreateAsync(_context, new VolumeCreateConfig
            {
                Name = volumeName
            });
            Assert.True(createResponse.Success);

            // Act
            var inspectResponse = await _driver.InspectAsync(_context, volumeName);

            // Assert
            Assert.True(inspectResponse.Success, inspectResponse.Error);
            Assert.NotNull(inspectResponse.Data);
            Assert.Equal(volumeName, inspectResponse.Data.Name);

            // Cleanup
            await _driver.RemoveAsync(_context, volumeName, force: false);
        }

        [Fact]
        public async Task Volume_Remove_RemovesVolume()
        {
            // Arrange
            var volumeName = $"test-remove-{Guid.NewGuid():N}";
            var createResponse = await _driver.CreateAsync(_context, new VolumeCreateConfig
            {
                Name = volumeName
            });
            Assert.True(createResponse.Success);

            // Act
            var removeResponse = await _driver.RemoveAsync(_context, volumeName, force: false);

            // Assert
            Assert.True(removeResponse.Success, removeResponse.Error);
        }

        [Fact]
        public async Task Volume_CreateWithLabels_SetsLabels()
        {
            // Arrange
            var volumeName = $"test-labels-{Guid.NewGuid():N}";
            var config = new VolumeCreateConfig
            {
                Name = volumeName,
                Labels = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "test.label", "test-value" },
                    { "environment", "testing" }
                }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, volumeName, force: false);
        }

        [Fact]
        public async Task Volume_CreateWithDriver_UsesCustomDriver()
        {
            // Arrange
            var volumeName = $"test-driver-{Guid.NewGuid():N}";
            var config = new VolumeCreateConfig
            {
                Name = volumeName,
                Driver = "local",
                DriverOpts = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "type", "tmpfs" },
                    { "device", "tmpfs" }
                }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, volumeName, force: false);
        }

        [Fact]
        public async Task Volume_Remove_InUse_Fails()
        {
            // Arrange
            var volumeName = $"test-inuse-{Guid.NewGuid():N}";
            var createVolumeResponse = await _driver.CreateAsync(_context, new VolumeCreateConfig
            {
                Name = volumeName
            });
            Assert.True(createVolumeResponse.Success);

            // Create container using the volume
            var containerDriver = _kernel.SysCtl<IContainerDriver>("docker-local");
            var containerResponse = await containerDriver.CreateAsync(_context, new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}",
                Volumes = new System.Collections.Generic.Dictionary<string, string>
                {
                    { volumeName, "/data" }
                }
            });
            Assert.True(containerResponse.Success);

            // Act
            var removeResponse = await _driver.RemoveAsync(_context, volumeName, force: false);

            // Assert
            Assert.False(removeResponse.Success);
            Assert.NotNull(removeResponse.Error);

            // Cleanup
            await containerDriver.RemoveAsync(_context, containerResponse.Data.Id, force: true);
            await _driver.RemoveAsync(_context, volumeName, force: false);
        }

        [Fact]
        public async Task Volume_Prune_RemovesUnusedVolumes()
        {
            // Arrange
            var volumeName = $"test-prune-{Guid.NewGuid():N}";
            var createResponse = await _driver.CreateAsync(_context, new VolumeCreateConfig
            {
                Name = volumeName
            });
            Assert.True(createResponse.Success);

            // Act
            var pruneResponse = await _driver.PruneAsync(_context);

            // Assert
            Assert.True(pruneResponse.Success, pruneResponse.Error);
            Assert.NotNull(pruneResponse.Data);
        }

        [Fact]
        public async Task Volume_Inspect_NonExistent_Fails()
        {
            // Arrange
            var nonExistentName = "nonexistent-volume-12345";

            // Act
            var response = await _driver.InspectAsync(_context, nonExistentName);

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }
    }
}
