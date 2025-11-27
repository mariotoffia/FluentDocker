using System;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using Xunit;

namespace FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class KernelIntegrationTests : IDisposable
    {
        private FluentDockerKernel _kernel;

        public void Dispose()
        {
            _kernel?.Dispose();
        }

        [Fact]
        public async Task KernelBuilder_WithDockerCli_CreatesKernel()
        {
            // Act
            _kernel = await new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Assert
            Assert.NotNull(_kernel);
            Assert.True(_kernel.IsDriverRegistered("docker-local"));
        }

        [Fact]
        public async Task KernelBuilder_MultipleDrivers_AllRegistered()
        {
            // Act
            _kernel = await new KernelBuilder()
                .WithDriver("docker-1", b => b.UseDockerCli())
                .WithDriver("docker-2", b => b.UseDockerCli())
                .BuildAsync();

            // Assert
            Assert.NotNull(_kernel);
            Assert.True(_kernel.IsDriverRegistered("docker-1"));
            Assert.True(_kernel.IsDriverRegistered("docker-2"));
        }

        [Fact]
        public async Task Kernel_SysCtl_ReturnsDriverInstance()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Act
            var driver = _kernel.SysCtl<Drivers.IContainerDriver>("docker-local");

            // Assert
            Assert.NotNull(driver);
            Assert.IsAssignableFrom<Drivers.IContainerDriver>(driver);
        }

        [Fact]
        public async Task Kernel_SysCtl_UnregisteredDriver_ThrowsException()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                _kernel.SysCtl<Drivers.IContainerDriver>("non-existent"));
        }

        [Fact]
        public async Task Kernel_Dispose_CleansUpResources()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Act
            _kernel.Dispose();

            // Assert
            // No exception should be thrown
            Assert.True(true);
        }
    }
}
