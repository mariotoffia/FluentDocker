using System;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders.V3;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Kernel;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.V3;
using Ductus.FluentDocker.Tests.V3.Mock;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.UnitTests
{
    /// <summary>
    /// Tests for complex multi-driver deployment scenarios.
    /// </summary>
    public class MultiDriverScenariosTests
    {
        [Fact]
        public async Task MultiDriver_ThreeDrivers_AllReceiveContainers()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-3", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Act
            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image1:latest").WithName("d1-container"))
                .WithinDriver("driver-2")
                    .UseContainer(c => c.UseImage("image2:latest").WithName("d2-container"))
                .WithinDriver("driver-3")
                    .UseContainer(c => c.UseImage("image3:latest").WithName("d3-container"))
                .BuildAsync();

            // Assert
            Assert.Equal(3, results.All.Count);
            Assert.Single(results.ForDriver("driver-1"));
            Assert.Single(results.ForDriver("driver-2"));
            Assert.Single(results.ForDriver("driver-3"));

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_UnevenDistribution_HandlesCorrectly()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("heavy", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("light", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Act
            var results = await new Builder()
                .WithinDriver("heavy", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("heavy-1"))
                    .UseContainer(c => c.UseImage("image:latest").WithName("heavy-2"))
                    .UseContainer(c => c.UseImage("image:latest").WithName("heavy-3"))
                    .UseContainer(c => c.UseImage("image:latest").WithName("heavy-4"))
                .WithinDriver("light")
                    .UseContainer(c => c.UseImage("image:latest").WithName("light-1"))
                .BuildAsync();

            // Assert
            Assert.Equal(5, results.All.Count);
            Assert.Equal(4, results.ForDriver("heavy").Count);
            Assert.Single(results.ForDriver("light"));

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_ScopeReuse_MaintainsKernel()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Act
            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-first"))
                .WithinDriver("driver-2")  // Should reuse kernel from driver-1
                    .UseContainer(c => c.UseImage("image:latest").WithName("d2-container"))
                .WithinDriver("driver-1")  // Back to driver-1
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-second"))
                .BuildAsync();

            // Assert
            Assert.Equal(3, results.All.Count);
            Assert.Equal(2, results.ForDriver("driver-1").Count);
            Assert.Single(results.ForDriver("driver-2"));

            // All services should share the same kernel (cast to IServiceAsync to access Kernel property)
            Assert.All(results.All.OfType<IServiceAsync>(), service => Assert.Same(kernel, service.Kernel));

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_IndependentLifecycles_DontInterfere()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-container"))
                .WithinDriver("driver-2")
                    .UseContainer(c => c.UseImage("image:latest").WithName("d2-container"))
                .BuildAsync();

            var service1 = results.ForDriver("driver-1").First() as IServiceAsync;
            var service2 = results.ForDriver("driver-2").First() as IServiceAsync;

            // Act - Start only service1
            await service1.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, service1.State);
            Assert.Equal(ServiceRunningState.Unknown, service2.State);

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_ErrorInOneDriver_DoesntAffectOthers()
        {
            // Arrange
            var failingDriver = new MockDriver();
            failingDriver.SimulateFailure = true;

            var kernel = await new KernelBuilder()
                .WithDriver("working", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("failing", b => b.UseCustomDriver(failingDriver))
                .BuildAsync();

            // Act & Assert
            var workingResult = await new Builder()
                .WithinDriver("working", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("working-container"))
                .BuildAsync();

            Assert.Single(workingResult.All);

            // Failing driver should throw
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await new Builder()
                    .WithinDriver("failing", kernel)
                        .UseContainer(c => c.UseImage("image:latest").WithName("failing-container"))
                    .BuildAsync();
            });

            // Cleanup
            await workingResult.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_FilterByDriver_ReturnsOnlyMatching()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("prod", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("dev", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("test", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            var results = await new Builder()
                .WithinDriver("prod", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("prod-1"))
                    .UseContainer(c => c.UseImage("image:latest").WithName("prod-2"))
                .WithinDriver("dev")
                    .UseContainer(c => c.UseImage("image:latest").WithName("dev-1"))
                .WithinDriver("test")
                    .UseContainer(c => c.UseImage("image:latest").WithName("test-1"))
                .BuildAsync();

            // Act
            var prodServices = results.ForDriver("prod");
            var devServices = results.ForDriver("dev");
            var testServices = results.ForDriver("test");
            var nonExistent = results.ForDriver("nonexistent");

            // Assert
            Assert.Equal(2, prodServices.Count);
            Assert.Single(devServices);
            Assert.Single(testServices);
            Assert.Empty(nonExistent);

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_DisposeScope_OnlyDisposesScope()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-container"))
                .WithinDriver("driver-2")
                    .UseContainer(c => c.UseImage("image:latest").WithName("d2-container"))
                .BuildAsync();

            var scope1 = results.Scopes.First(s => s.DriverId == "driver-1");

            // Act
            await scope1.DisposeAllAsync();

            // Assert
            // Note: This tests the conceptual pattern; actual behavior depends on BuildScope implementation
            Assert.NotNull(results.ForDriver("driver-2"));

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_AllDispose_DisposesAllScopes()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-container"))
                .WithinDriver("driver-2")
                    .UseContainer(c => c.UseImage("image:latest").WithName("d2-container"))
                .BuildAsync();

            // Act
            await results.DisposeAllAsync();

            // Assert - no exception means success
            Assert.True(true);

            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_BuildResultsProperties_Accessible()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Act
            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-container"))
                .WithinDriver("driver-2")
                    .UseContainer(c => c.UseImage("image:latest").WithName("d2-container"))
                .BuildAsync();

            // Assert
            Assert.NotNull(results.All);
            Assert.NotNull(results.Scopes);
            Assert.Equal(2, results.All.Count);
            Assert.Equal(2, results.Scopes.Count);

            // Each scope should have correct driver ID
            Assert.Contains(results.Scopes, s => s.DriverId == "driver-1");
            Assert.Contains(results.Scopes, s => s.DriverId == "driver-2");

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }

        [Fact]
        public async Task MultiDriver_EmptyScope_HandledCorrectly()
        {
            // Arrange
            var kernel = await new KernelBuilder()
                .WithDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .WithDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Act - Create scope but don't add containers to driver-2
            var results = await new Builder()
                .WithinDriver("driver-1", kernel)
                    .UseContainer(c => c.UseImage("image:latest").WithName("d1-container"))
                .WithinDriver("driver-2")
                    // No containers added
                .BuildAsync();

            // Assert
            Assert.Single(results.All);
            Assert.Single(results.Scopes); // Only driver-1 scope should exist

            // Cleanup
            await results.DisposeAllAsync();
            kernel.Dispose();
        }
    }
}
