using System.Threading.Tasks;
using Ductus.FluentDocker.Builders.V3;
using Ductus.FluentDocker.Kernel;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3
{
    /// <summary>
    /// Tests for v3.0.0 async Builder with WithinDriver() scoping.
    /// </summary>
    public class BuilderTests
    {
        [Fact(Skip = "Integration test - requires Docker daemon and Phase 5 implementation")]
        public async Task Builder_WithSingleContainer_CreatesBuildResults()
        {
            // Arrange
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli())
                .BuildAsync();

            try
            {
                // Act
                var results = await new Builder()
                    .WithinDriver("docker", kernel)
                        .UseContainer(c => c
                            .UseImage("nginx:latest")
                            .WithName("test-nginx"))
                    .BuildAsync();

                // Assert
                Assert.NotNull(results);
                Assert.Single(results.All);
                Assert.Single(results.ForDriver("docker"));

                // Cleanup
                await results.DisposeAllAsync();
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact(Skip = "Integration test - requires Docker daemon and Phase 5 implementation")]
        public async Task Builder_WithMultipleScopes_CreatesBuildResultsForEachScope()
        {
            // Arrange
            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker-1", d => d.UseDockerCli())
                .WithDriver("docker-2", d => d.UseDockerCli())
                .BuildAsync();

            try
            {
                // Act
                var results = await new Builder()
                    .WithinDriver("docker-1", kernel)
                        .UseContainer(c => c.UseImage("nginx"))
                        .UseContainer(c => c.UseImage("postgres"))
                    .WithinDriver("docker-2")  // Reuses kernel
                        .UseContainer(c => c.UseImage("redis"))
                    .BuildAsync();

                // Assert
                Assert.NotNull(results);
                Assert.Equal(3, results.All.Count);
                Assert.Equal(2, results.ForDriver("docker-1").Count);
                Assert.Single(results.ForDriver("docker-2"));

                // Cleanup
                await results.DisposeAllAsync();
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public void Builder_VerifiesCompilation()
        {
            // This test just verifies the v3 Builder API compiles correctly
            var builder = new Builder();
            Assert.NotNull(builder);
        }

        [Fact]
        public async Task Builder_KernelReuse_VerifiesPattern()
        {
            // This test demonstrates the kernel reuse pattern
            // Actual execution requires Docker daemon and Phase 5 implementation

            var kernel = await FluentDockerKernel.Create()
                .WithDriver("docker-1", d => d.UseDockerCli())
                .WithDriver("docker-2", d => d.UseDockerCli())
                .BuildAsync();

            var builder = new Builder()
                .WithinDriver("docker-1", kernel)  // Sets kernel
                    // .UseContainer(...) would go here
                .WithinDriver("docker-2");  // Reuses kernel from previous WithinDriver()

            Assert.NotNull(builder);

            kernel.Dispose();
        }
    }
}
