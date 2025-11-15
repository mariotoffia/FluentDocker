using System;
using System.Threading.Tasks;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Tests.V3.Mock;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.Unit
{
    /// <summary>
    /// Tests for v3.0.0 kernel isolation and multi-kernel scenarios.
    /// </summary>
    public class KernelIsolationTests : IDisposable
    {
        private FluentDockerKernel _kernel1;
        private FluentDockerKernel _kernel2;

        public void Dispose()
        {
            _kernel1?.Dispose();
            _kernel2?.Dispose();
        }

        [Fact]
        public async Task MultipleKernels_IndependentRegistries_DontInterfere()
        {
            // Arrange & Act
            _kernel1 = await new KernelBuilder()
                .UseDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            _kernel2 = await new KernelBuilder()
                .UseDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Assert
            Assert.True(_kernel1.IsDriverRegistered("driver-1"));
            Assert.False(_kernel1.IsDriverRegistered("driver-2"));

            Assert.True(_kernel2.IsDriverRegistered("driver-2"));
            Assert.False(_kernel2.IsDriverRegistered("driver-1"));
        }

        [Fact]
        public async Task MultipleKernels_SameDriverId_AreIndependent()
        {
            // Arrange
            var driver1 = new MockDriver();
            var driver2 = new MockDriver();

            // Act
            _kernel1 = await new KernelBuilder()
                .UseDriver("shared-id", b => b.UseCustomDriver(driver1))
                .BuildAsync();

            _kernel2 = await new KernelBuilder()
                .UseDriver("shared-id", b => b.UseCustomDriver(driver2))
                .BuildAsync();

            var retrieved1 = _kernel1.SysCtl<Drivers.IDriver>("shared-id");
            var retrieved2 = _kernel2.SysCtl<Drivers.IDriver>("shared-id");

            // Assert
            Assert.Same(driver1, retrieved1);
            Assert.Same(driver2, retrieved2);
            Assert.NotSame(retrieved1, retrieved2);
        }

        [Fact]
        public async Task Kernel_Dispose_DoesNotAffectOtherKernels()
        {
            // Arrange
            _kernel1 = await new KernelBuilder()
                .UseDriver("driver-1", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            _kernel2 = await new KernelBuilder()
                .UseDriver("driver-2", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Act
            _kernel1.Dispose();
            _kernel1 = null; // Prevent double dispose

            // Assert
            Assert.True(_kernel2.IsDriverRegistered("driver-2"));
            var driver = _kernel2.SysCtl<Drivers.IDriver>("driver-2");
            Assert.NotNull(driver);
        }

        [Fact]
        public async Task Kernel_MultipleDrivers_AllAccessible()
        {
            // Arrange & Act
            _kernel1 = await new KernelBuilder()
                .UseDriver("driver-a", b => b.UseCustomDriver(new MockDriver()))
                .UseDriver("driver-b", b => b.UseCustomDriver(new MockDriver()))
                .UseDriver("driver-c", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            // Assert
            Assert.True(_kernel1.IsDriverRegistered("driver-a"));
            Assert.True(_kernel1.IsDriverRegistered("driver-b"));
            Assert.True(_kernel1.IsDriverRegistered("driver-c"));

            Assert.NotNull(_kernel1.SysCtl<Drivers.IDriver>("driver-a"));
            Assert.NotNull(_kernel1.SysCtl<Drivers.IDriver>("driver-b"));
            Assert.NotNull(_kernel1.SysCtl<Drivers.IDriver>("driver-c"));
        }

        [Fact]
        public async Task Kernel_DriverIsolation_StateNotShared()
        {
            // Arrange
            var driverA = new MockDriver();
            var driverB = new MockDriver();

            _kernel1 = await new KernelBuilder()
                .UseDriver("driver-a", b => b.UseCustomDriver(driverA))
                .UseDriver("driver-b", b => b.UseCustomDriver(driverB))
                .BuildAsync();

            // Act - Create container in driver-a
            var containerDriver = _kernel1.SysCtl<Drivers.IContainerDriver>("driver-a");
            var context = new Model.Drivers.DriverContext("driver-a");
            await containerDriver.CreateAsync(context, new Drivers.ContainerCreateConfig
            {
                Image = "test:latest",
                Name = "test-container"
            });

            // Assert - Driver B should not have the container
            var driverBState = driverB as MockDriver;
            Assert.NotNull(driverBState);
            Assert.Empty(driverBState.Containers); // Driver B has no containers

            var driverAState = driverA as MockDriver;
            Assert.NotNull(driverAState);
            Assert.Single(driverAState.Containers); // Driver A has one container
        }

        [Fact]
        public async Task KernelBuilder_ConcurrentBuilds_CreateIndependentKernels()
        {
            // Arrange & Act
            var buildTasks = new[]
            {
                new KernelBuilder()
                    .UseDriver("concurrent-1", b => b.UseCustomDriver(new MockDriver()))
                    .BuildAsync(),
                new KernelBuilder()
                    .UseDriver("concurrent-2", b => b.UseCustomDriver(new MockDriver()))
                    .BuildAsync(),
                new KernelBuilder()
                    .UseDriver("concurrent-3", b => b.UseCustomDriver(new MockDriver()))
                    .BuildAsync()
            };

            var kernels = await Task.WhenAll(buildTasks);

            try
            {
                // Assert
                Assert.Equal(3, kernels.Length);
                Assert.All(kernels, k => Assert.NotNull(k));

                Assert.True(kernels[0].IsDriverRegistered("concurrent-1"));
                Assert.False(kernels[0].IsDriverRegistered("concurrent-2"));

                Assert.True(kernels[1].IsDriverRegistered("concurrent-2"));
                Assert.False(kernels[1].IsDriverRegistered("concurrent-1"));

                Assert.True(kernels[2].IsDriverRegistered("concurrent-3"));
                Assert.False(kernels[2].IsDriverRegistered("concurrent-1"));
            }
            finally
            {
                foreach (var kernel in kernels)
                {
                    kernel?.Dispose();
                }
            }
        }

        [Fact]
        public async Task Kernel_SameDriverInstance_SharedAcrossKernel()
        {
            // Arrange
            var sharedDriver = new MockDriver();

            // Act
            _kernel1 = await new KernelBuilder()
                .UseDriver("shared", b => b.UseCustomDriver(sharedDriver))
                .UseDriver("other", b => b.UseCustomDriver(new MockDriver()))
                .BuildAsync();

            var retrieved1 = _kernel1.SysCtl<Drivers.IDriver>("shared");
            var retrieved2 = _kernel1.SysCtl<Drivers.IDriver>("shared");

            // Assert
            Assert.Same(sharedDriver, retrieved1);
            Assert.Same(sharedDriver, retrieved2);
            Assert.Same(retrieved1, retrieved2);
        }

        [Fact]
        public async Task Kernel_DifferentDriverTypes_CoexistCorrectly()
        {
            // Arrange
            var mockDriver = new MockDriver();

            // Act
            _kernel1 = await new KernelBuilder()
                .UseDriver("mock", b => b.UseCustomDriver(mockDriver))
                .BuildAsync();

            // Assert
            var containerDriver = _kernel1.SysCtl<Drivers.IContainerDriver>("mock");
            var imageDriver = _kernel1.SysCtl<Drivers.IImageDriver>("mock");
            var networkDriver = _kernel1.SysCtl<Drivers.INetworkDriver>("mock");

            Assert.NotNull(containerDriver);
            Assert.NotNull(imageDriver);
            Assert.NotNull(networkDriver);

            // All should be the same instance (MockDriver implements all)
            Assert.Same(containerDriver, imageDriver);
            Assert.Same(imageDriver, networkDriver);
        }
    }
}
