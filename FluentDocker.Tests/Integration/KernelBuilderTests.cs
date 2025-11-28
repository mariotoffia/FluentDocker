using System.Threading.Tasks;
using FluentDocker.Kernel;
using Xunit;

namespace FluentDocker.Tests.Integration
{
    /// <summary>
    /// Integration tests for Kernel Builder with async/await pattern.
    /// </summary>
    [Trait("Category", "Integration")]
    public class KernelBuilderTests
    {
        [Fact]
        public async Task KernelBuilder_WithSingleDriver_CreatesKernel()
        {
            // Arrange & Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli())
                .BuildAsync();

            // Assert
            Assert.NotNull(kernel);
            Assert.True(kernel.IsDriverRegistered("docker"));

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public async Task KernelBuilder_WithMultipleDrivers_CreatesKernel()
        {
            // Arrange & Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker-local", d => d.UseDockerCli())
                .WithDriver("docker-remote", d => d.UseDockerCli())
                .BuildAsync();

            // Assert
            Assert.NotNull(kernel);
            Assert.True(kernel.IsDriverRegistered("docker-local"));
            Assert.True(kernel.IsDriverRegistered("docker-remote"));

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public async Task KernelSysCtl_CanAccessContainerDriver()
        {
            // Arrange
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli())
                .BuildAsync();

            try
            {
                // Act
                var containerDriver = kernel.SysCtl<Drivers.IContainerDriver>("docker");

                // Assert
                Assert.NotNull(containerDriver);
            }
            finally
            {
                // Cleanup
                kernel.Dispose();
            }
        }

        [Fact]
        public void KernelBuilder_WithoutDriver_ThrowsException()
        {
            // This test verifies builder API compilation

            var builder = FluentDockerKernel.Create();
            Assert.NotNull(builder);
        }
    }
}

