using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
    /// <summary>
    /// Unit tests for CapabilityChecks.
    /// </summary>
    [Trait("Category", "Unit")]
    public class CapabilityChecksTests
    {
        [Fact]
        public async Task EnsureContainerSupportAsync_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsContainers = true });

            try
            {
                // Act & Assert - should not throw
                await CapabilityChecks.EnsureContainerSupportAsync(kernel, "docker");
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureContainerSupportAsync_WhenNotSupported_ThrowsCapabilityNotSupportedException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsContainers = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    CapabilityChecks.EnsureContainerSupportAsync(kernel, "docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureNetworkSupportAsync_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsNetworks = true });

            try
            {
                // Act & Assert - should not throw
                await CapabilityChecks.EnsureNetworkSupportAsync(kernel, "docker");
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureNetworkSupportAsync_WhenNotSupported_ThrowsCapabilityNotSupportedException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsNetworks = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    CapabilityChecks.EnsureNetworkSupportAsync(kernel, "docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureVolumeSupportAsync_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsVolumes = true });

            try
            {
                // Act & Assert - should not throw
                await CapabilityChecks.EnsureVolumeSupportAsync(kernel, "docker");
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureVolumeSupportAsync_WhenNotSupported_ThrowsCapabilityNotSupportedException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsVolumes = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    CapabilityChecks.EnsureVolumeSupportAsync(kernel, "docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureComposeSupportAsync_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsCompose = true });

            try
            {
                // Act & Assert - should not throw
                await CapabilityChecks.EnsureComposeSupportAsync(kernel, "docker");
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureComposeSupportAsync_WhenNotSupported_ThrowsCapabilityNotSupportedException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsCompose = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    CapabilityChecks.EnsureComposeSupportAsync(kernel, "docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureImageSupportAsync_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsImages = true });

            try
            {
                // Act & Assert - should not throw
                await CapabilityChecks.EnsureImageSupportAsync(kernel, "docker");
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureImageSupportAsync_WhenNotSupported_ThrowsCapabilityNotSupportedException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsImages = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    CapabilityChecks.EnsureImageSupportAsync(kernel, "docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsurePodSupportAsync_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsPods = true });

            try
            {
                // Act & Assert - should not throw
                await CapabilityChecks.EnsurePodSupportAsync(kernel, "docker");
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsurePodSupportAsync_WhenNotSupported_ThrowsCapabilityNotSupportedException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsPods = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    CapabilityChecks.EnsurePodSupportAsync(kernel, "docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task GetCapabilitiesAsync_ReturnsCapabilities()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            var expectedCapabilities = new DriverCapabilities 
            { 
                SupportsContainers = true,
                SupportsImages = true,
                SupportsNetworks = true,
                SupportsVolumes = true,
                SupportsCompose = true,
                Version = "24.0.0"
            };
            mockPack.SetCapabilities(expectedCapabilities);

            try
            {
                // Act
                var capabilities = await CapabilityChecks.GetCapabilitiesAsync(kernel, "docker");

                // Assert
                Assert.True(capabilities.SupportsContainers);
                Assert.True(capabilities.SupportsImages);
                Assert.True(capabilities.SupportsNetworks);
                Assert.True(capabilities.SupportsVolumes);
                Assert.True(capabilities.SupportsCompose);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task IsHealthyAsync_WhenHealthy_ReturnsTrue()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetHealthy(true);

            try
            {
                // Act
                var isHealthy = await CapabilityChecks.IsHealthyAsync(kernel, "docker");

                // Assert
                Assert.True(isHealthy);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task IsHealthyAsync_WhenUnhealthy_ReturnsFalse()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetHealthy(false);

            try
            {
                // Act
                var isHealthy = await CapabilityChecks.IsHealthyAsync(kernel, "docker");

                // Assert
                Assert.False(isHealthy);
            }
            finally
            {
                kernel.Dispose();
            }
        }
    }

    /// <summary>
    /// Unit tests for KernelCapabilityExtensions.
    /// </summary>
    [Trait("Category", "Unit")]
    public class KernelCapabilityExtensionsTests
    {
        [Fact]
        public async Task EnsureCapabilityAsync_Container_WhenSupported_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsContainers = true });

            try
            {
                // Act & Assert - should not throw
                await kernel.EnsureCapabilityAsync("docker", DriverCapability.Container);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureCapabilityAsync_Network_WhenNotSupported_ThrowsException()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(new DriverCapabilities { SupportsNetworks = false });

            try
            {
                // Act & Assert
                await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
                    kernel.EnsureCapabilityAsync("docker", DriverCapability.Network));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task EnsureCapabilityAsync_AllCapabilities_WhenSupported()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetCapabilities(DriverCapabilities.Default());

            try
            {
                // Act & Assert - all should not throw
                await kernel.EnsureCapabilityAsync("docker", DriverCapability.Container);
                await kernel.EnsureCapabilityAsync("docker", DriverCapability.Image);
                await kernel.EnsureCapabilityAsync("docker", DriverCapability.Network);
                await kernel.EnsureCapabilityAsync("docker", DriverCapability.Volume);
                await kernel.EnsureCapabilityAsync("docker", DriverCapability.Compose);
            }
            finally
            {
                kernel.Dispose();
            }
        }
    }
}
