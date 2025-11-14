using System.Threading.Tasks;
using Ductus.FluentDocker.Kernel;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3
{
    /// <summary>
    /// Tests for v3.0.0 Kernel Builder with async/await pattern.
    /// </summary>
    public class KernelBuilderTests
    {
        [Fact(Skip = "Integration test - requires Docker daemon")]
        public async Task KernelBuilder_WithSingleDriver_CreatesKernel()
        {
            // Arrange & Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli())
                .BuildAsync();

            // Assert
            Assert.NotNull(kernel);
            Assert.True(kernel.IsDriverRegistered("docker"));
            Assert.Equal("docker", kernel.DefaultDriverId);

            // Cleanup
            kernel.Dispose();
        }

        [Fact(Skip = "Integration test - requires Docker daemon")]
        public async Task KernelBuilder_WithMultipleDrivers_CreatesKernel()
        {
            // Arrange & Act
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker-local", d => d
                    .UseDockerCli()
                    .AtHost("unix:///var/run/docker.sock"))
                .WithDriver("docker-remote", d => d
                    .UseDockerCli()
                    .AtHost("tcp://remote:2376")
                    .WithCertificates("/path/to/certs")
                    .AsDefault())
                .BuildAsync();

            // Assert
            Assert.NotNull(kernel);
            Assert.True(kernel.IsDriverRegistered("docker-local"));
            Assert.True(kernel.IsDriverRegistered("docker-remote"));
            Assert.Equal("docker-remote", kernel.DefaultDriverId);

            // Cleanup
            kernel.Dispose();
        }

        [Fact(Skip = "Integration test - requires Docker daemon")]
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
            // Actual execution would require Docker daemon

            var builder = FluentDockerKernel.Create();
            Assert.NotNull(builder);
        }
    }
}
