using System;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
    /// <summary>
    /// Tests for TrySysCtl, SysCtl(driverId, Type), and backward-compatible
    /// SysCtl(driverId, DriverComponent) on FluentDockerKernel.
    /// </summary>
    public class TrySysCtlTests
    {
        private async Task<(FluentDockerKernel kernel, MockDriverPack pack)> CreateKernelWithMockPack()
        {
            var pack = new MockDriverPack();
            var kernel = new FluentDockerKernel();
            await kernel.RegisterDriverPackAsync("test", pack, new DriverContext("test"));
            return (kernel, pack);
        }

        [Fact]
        public async Task TrySysCtl_ExistingInterface_ReturnsTrueAndInstance()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();

            // Act
            var result = kernel.TrySysCtl<IContainerDriver>("test", out var instance);

            // Assert
            Assert.True(result);
            Assert.NotNull(instance);
        }

        [Fact]
        public async Task TrySysCtl_NonExistingInterface_ReturnsFalseAndNull()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();

            // Act
            var result = kernel.TrySysCtl<IDisposable>("test", out var instance);

            // Assert
            Assert.False(result);
            Assert.Null(instance);
        }

        [Fact]
        public async Task TrySysCtl_NonExistingDriver_ThrowsDriverNotFound()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();

            // Act & Assert
            Assert.Throws<DriverNotFoundException>(() =>
                kernel.TrySysCtl<IContainerDriver>("non-existent", out _));
        }

        [Fact]
        public async Task TrySysCtl_AfterDispose_ThrowsObjectDisposed()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();
            kernel.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() =>
                kernel.TrySysCtl<IContainerDriver>("test", out _));
        }

        [Fact]
        public async Task SysCtlByType_ExistingInterface_ReturnsObject()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();

            // Act
            var result = kernel.SysCtl("test", typeof(IContainerDriver));

            // Assert
            Assert.NotNull(result);
            Assert.IsAssignableFrom<IContainerDriver>(result);
        }

        [Fact]
        public async Task SysCtlByType_NonExistingInterface_ThrowsInterfaceNotSupported()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();

            // Act & Assert
            Assert.Throws<InterfaceNotSupportedException>(() =>
                kernel.SysCtl("test", typeof(IDisposable)));
        }

        [Fact]
        public async Task SysCtlByComponent_DelegatesToTypeResolution()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();

            // Act - use the legacy DriverComponent enum
            var container = kernel.SysCtl("test", DriverComponent.Container);
            var image = kernel.SysCtl("test", DriverComponent.Image);
            var network = kernel.SysCtl("test", DriverComponent.Network);
            var volume = kernel.SysCtl("test", DriverComponent.Volume);

            // Assert
            Assert.NotNull(container);
            Assert.IsAssignableFrom<IContainerDriver>(container);
            Assert.NotNull(image);
            Assert.IsAssignableFrom<IImageDriver>(image);
            Assert.NotNull(network);
            Assert.IsAssignableFrom<INetworkDriver>(network);
            Assert.NotNull(volume);
            Assert.IsAssignableFrom<IVolumeDriver>(volume);
        }

        [Fact]
        public async Task TrySysCtl_CustomInterface_AfterRegistration_Succeeds()
        {
            // Arrange
            var (kernel, pack) = await CreateKernelWithMockPack();
            var mockCustom = new Moq.Mock<ICustomKernelInterface>();
            pack.RegisterCustomDriver(mockCustom.Object);

            // Act
            var result = kernel.TrySysCtl<ICustomKernelInterface>("test", out var instance);

            // Assert
            Assert.True(result);
            Assert.NotNull(instance);
        }

        public interface ICustomKernelInterface
        {
            void Custom();
        }
    }
}
