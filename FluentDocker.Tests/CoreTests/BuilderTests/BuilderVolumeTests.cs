using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
    /// <summary>
    /// Unit tests for the v3 Builder with volume operations.
    /// Uses mock drivers to test without Docker daemon.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BuilderVolumeTests : MockKernelTestBase, IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            await InitializeMockKernelAsync();
        }

        public Task DisposeAsync()
        {
            return base.DisposeAsync().AsTask();
        }

        [Fact]
        public async Task UseVolume_WithName_CreatesVolume()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate("test-volume")
                .SetupVolumeRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName("test-volume"))
                .BuildAsync();

            // Assert
            Assert.NotNull(results);
            Assert.Single(results.All);
            var volume = results.All.First() as IVolumeService;
            Assert.NotNull(volume);
            Assert.Equal("test-volume", volume.VolumeName);

            MockPack.VerifyVolumeCreated("test-volume", Times.Once());
        }

        [Fact]
        public async Task UseVolume_WithDriver_PassesDriver()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate()
                .SetupVolumeRemove();

            // Act
            await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName("nfs-volume")
                    .UseDriver("local"))
                .BuildAsync();

            // Assert
            MockPack.VolumeDriver.Verify(d => d.CreateAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.Is<VolumeCreateConfig>(cfg => cfg.Driver == "local"),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UseVolume_WithDriverOptions_PassesOptions()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate()
                .SetupVolumeRemove();

            // Act
            await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName("nfs-volume")
                    .UseDriver("local")
                    .WithDriverOption("type", "nfs")
                    .WithDriverOption("o", "addr=nfs-server,rw")
                    .WithDriverOption("device", ":/path/to/dir"))
                .BuildAsync();

            // Assert
            MockPack.VolumeDriver.Verify(d => d.CreateAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.Is<VolumeCreateConfig>(cfg =>
                    cfg.DriverOpts != null &&
                    cfg.DriverOpts.ContainsKey("type") &&
                    cfg.DriverOpts["type"] == "nfs"),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UseVolume_WithLabels_PassesLabels()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate()
                .SetupVolumeRemove();

            // Act
            await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName("labeled-volume")
                    .WithLabel("backup", "true")
                    .WithLabel("retention", "7d"))
                .BuildAsync();

            // Assert
            MockPack.VolumeDriver.Verify(d => d.CreateAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.Is<VolumeCreateConfig>(cfg =>
                    cfg.Labels != null &&
                    cfg.Labels["backup"] == "true" &&
                    cfg.Labels["retention"] == "7d"),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UseVolume_RemoveOnDispose_RemovesOnDispose()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate("temp-volume")
                .SetupVolumeRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName("temp-volume")
                    .RemoveOnDispose())
                .BuildAsync();

            await results.DisposeAllAsync();

            // Assert - Remove should be called
            MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.Is<string>(name => name == "temp-volume"),
                It.IsAny<bool>(),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UseVolume_WithoutRemoveOnDispose_DoesNotRemove()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate("persistent-volume")
                .SetupVolumeRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v
                    .WithName("persistent-volume"))
                .BuildAsync();

            await results.DisposeAllAsync();

            // Assert - Remove should NOT be called
            MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UseVolume_MultipleVolumes_CreatesAll()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate()
                .SetupVolumeRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v.WithName("data-volume"))
                .UseVolume(v => v.WithName("config-volume"))
                .UseVolume(v => v.WithName("logs-volume"))
                .BuildAsync();

            // Assert
            Assert.Equal(3, results.All.Count());
            MockPack.VolumeDriver.Verify(d => d.CreateAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.IsAny<VolumeCreateConfig>(),
                It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task UseVolume_LocalDriver_IsDefault()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate()
                .SetupVolumeRemove();

            // Act
            await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v.WithName("default-driver-volume"))
                .BuildAsync();

            // Assert - Default driver should be "local"
            MockPack.VolumeDriver.Verify(d => d.CreateAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.Is<VolumeCreateConfig>(cfg => cfg.Driver == "local"),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
    }
}
