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
    /// Unit tests for FluentDockerKernel.
    /// </summary>
    [Trait("Category", "Unit")]
    public class FluentDockerKernelTests
    {
        [Fact]
        public void Create_ReturnsKernelBuilder()
        {
            // Act
            var builder = FluentDockerKernel.Create();

            // Assert
            Assert.NotNull(builder);
            Assert.IsType<KernelBuilder>(builder);
        }

        [Fact]
        public async Task RegisterDriverPackAsync_RegistersSuccessfully()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var mockPack = new MockDriverPack();
            var context = new DriverContext("docker");

            // Act
            await kernel.RegisterDriverPackAsync("docker", mockPack, context);

            // Assert
            Assert.True(kernel.IsDriverRegistered("docker"));
        }

        [Fact]
        public async Task SysCtl_ReturnsCorrectInterface()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

            try
            {
                // Act
                var containerDriver = kernel.SysCtl<IContainerDriver>("docker");

                // Assert
                Assert.NotNull(containerDriver);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task SysCtl_NonExistentDriver_ThrowsException()
        {
            // Arrange
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

            try
            {
                // Act & Assert
                Assert.Throws<DriverNotFoundException>(() =>
                    kernel.SysCtl<IContainerDriver>("non-existent"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task SysCtl_ByComponent_ReturnsCorrectInterface()
        {
            // Arrange
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

            try
            {
                // Act
                var driver = kernel.SysCtl("docker", DriverComponent.Container);

                // Assert
                Assert.NotNull(driver);
                Assert.IsAssignableFrom<IContainerDriver>(driver);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task SetDefaultDriver_SetsDefault()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var mockPack1 = new MockDriverPack();
            var mockPack2 = new MockDriverPack();
            await mockPack1.InitializeAsync(new DriverContext("driver1"));
            await mockPack2.InitializeAsync(new DriverContext("driver2"));

            await kernel.RegisterDriverPackAsync("driver1", mockPack1, new DriverContext("driver1"));
            await kernel.RegisterDriverPackAsync("driver2", mockPack2, new DriverContext("driver2"));

            // Act
            kernel.SetDefaultDriver("driver2");

            // Assert
            Assert.Equal("driver2", kernel.DefaultDriverId);

            kernel.Dispose();
        }

        [Fact]
        public async Task GetAllDriverIds_ReturnsAllRegistered()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var mockPack1 = new MockDriverPack();
            var mockPack2 = new MockDriverPack();
            await mockPack1.InitializeAsync(new DriverContext("docker-local"));
            await mockPack2.InitializeAsync(new DriverContext("docker-remote"));

            await kernel.RegisterDriverPackAsync("docker-local", mockPack1, new DriverContext("docker-local"));
            await kernel.RegisterDriverPackAsync("docker-remote", mockPack2, new DriverContext("docker-remote"));

            // Assert - both drivers are registered
            Assert.True(kernel.IsDriverRegistered("docker-local"));
            Assert.True(kernel.IsDriverRegistered("docker-remote"));
            Assert.False(kernel.IsDriverRegistered("nonexistent"));

            kernel.Dispose();
        }

        [Fact]
        public async Task UnregisterDriver_RemovesDriver()
        {
            // Arrange
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

            // Act
            kernel.UnregisterDriver("docker");

            // Assert
            Assert.False(kernel.IsDriverRegistered("docker"));

            kernel.Dispose();
        }

        [Fact]
        public void Dispose_MultipleCallsSafe()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act & Assert - should not throw
            kernel.Dispose();
            kernel.Dispose();
        }

        [Fact]
        public async Task SysCtl_AfterDispose_ThrowsException()
        {
            // Arrange
            var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            kernel.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() =>
                kernel.SysCtl<IContainerDriver>("docker"));
        }

        [Fact]
        public async Task MultipleKernelInstances_AreIndependent()
        {
            // Arrange
            var (kernel1, mockPack1) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            var (kernel2, mockPack2) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

            try
            {
                // Assert - kernels are different instances
                Assert.NotSame(kernel1, kernel2);
                Assert.NotSame(mockPack1, mockPack2);

                // Each kernel has its own driver
                var driver1 = kernel1.SysCtl<IContainerDriver>("docker");
                var driver2 = kernel2.SysCtl<IContainerDriver>("docker");
                Assert.NotSame(driver1, driver2);
            }
            finally
            {
                kernel1.Dispose();
                kernel2.Dispose();
            }
        }
    }

    /// <summary>
    /// Unit tests for KernelBuilder.
    /// </summary>
    [Trait("Category", "Unit")]
    public class KernelBuilderTests
    {
        [Fact]
        public async Task BuildAsync_WithDockerCli_CreatesKernel()
        {
            // Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli())
                .BuildAsync();

            try
            {
                // Assert
                Assert.NotNull(kernel);
                Assert.True(kernel.IsDriverRegistered("docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task BuildAsync_WithMultipleDrivers_RegistersAll()
        {
            // Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker-local", d => d.UseDockerCli())
                .WithDriver("docker-remote", d => d.UseDockerCli())
                .BuildAsync();

            try
            {
                // Assert
                Assert.True(kernel.IsDriverRegistered("docker-local"));
                Assert.True(kernel.IsDriverRegistered("docker-remote"));
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task BuildAsync_WithDefault_SetsDefault()
        {
            // Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli().AsDefault())
                .BuildAsync();

            try
            {
                // Assert
                Assert.Equal("docker", kernel.DefaultDriverId);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public void WithDriver_NullDriverId_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                FluentDockerKernel.Create()
                    .WithDriver(null!, d => d.UseDockerCli()));
        }

        [Fact]
        public void WithDriver_NullConfigure_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                FluentDockerKernel.Create()
                    .WithDriver("docker", null!));
        }

        [Fact]
        public void Build_Sync_CreatesKernel()
        {
            // Act
            var kernel = FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli())
                .Build();

            try
            {
                // Assert
                Assert.NotNull(kernel);
                Assert.True(kernel.IsDriverRegistered("docker"));
            }
            finally
            {
                kernel.Dispose();
            }
        }
    }
}
