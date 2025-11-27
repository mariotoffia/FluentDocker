using System;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Drivers;
using Xunit;
using Unit = Ductus.FluentDocker.Model.Drivers.Unit;

namespace Ductus.FluentDocker.Tests.V3.UnitTests
{
    [Trait("Category", "Unit")]
    public class FluentDockerKernelTests
    {
        [Fact]
        public void Constructor_CreatesKernel()
        {
            // Act
            var kernel = new FluentDockerKernel();

            // Assert
            Assert.NotNull(kernel);
        }

        [Fact]
        public async Task RegisterDriverAsync_SingleDriver_Registers()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockDriver();
            var context = new DriverContext("test-driver");

            // Act
            await kernel.RegisterDriverAsync("test-driver", driver, context);

            // Assert
            Assert.True(kernel.IsDriverRegistered("test-driver"));
        }

        [Fact]
        public async Task RegisterDriverAsync_MultipleDrivers_AllRegistered()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver1 = new MockDriver();
            var driver2 = new MockDriver();

            // Act
            await kernel.RegisterDriverAsync("driver-1", driver1, new DriverContext("driver-1"));
            await kernel.RegisterDriverAsync("driver-2", driver2, new DriverContext("driver-2"));

            // Assert
            Assert.True(kernel.IsDriverRegistered("driver-1"));
            Assert.True(kernel.IsDriverRegistered("driver-2"));
        }

        [Fact]
        public async Task UnregisterDriver_RemovesDriver()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockDriver();
            await kernel.RegisterDriverAsync("test-driver", driver, new DriverContext("test-driver"));

            // Act
            kernel.UnregisterDriver("test-driver");

            // Assert
            Assert.False(kernel.IsDriverRegistered("test-driver"));
        }

        [Fact]
        public async Task GetDriver_ReturnsRegisteredDriver()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockDriver();
            await kernel.RegisterDriverAsync("test-driver", driver, new DriverContext("test-driver"));

            // Act
            var retrieved = kernel.GetDriver("test-driver");

            // Assert
            Assert.Same(driver, retrieved);
        }

        [Fact]
        public void GetDriver_NonExistent_ThrowsException()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act & Assert
            Assert.Throws<DriverNotFoundException>(() =>
                kernel.GetDriver("non-existent"));
        }

        [Fact]
        public async Task SysCtl_TypeSafe_ReturnsTypedDriver()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockContainerDriver();
            await kernel.RegisterDriverAsync("test-driver", driver, new DriverContext("test-driver"));

            // Act
            var containerDriver = kernel.SysCtl<IContainerDriver>("test-driver");

            // Assert
            Assert.NotNull(containerDriver);
            Assert.Same(driver, containerDriver);
        }

        [Fact]
        public async Task SysCtl_NonExistentDriver_ThrowsException()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act & Assert
            Assert.Throws<DriverNotFoundException>(() =>
                kernel.SysCtl<IContainerDriver>("non-existent"));
        }

        [Fact]
        public async Task SysCtl_UnsupportedInterface_ThrowsException()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockDriver(); // Does not implement IContainerDriver
            await kernel.RegisterDriverAsync("test-driver", driver, new DriverContext("test-driver"));

            // Act & Assert
            Assert.Throws<InterfaceNotSupportedException>(() =>
                kernel.SysCtl<IContainerDriver>("test-driver"));
        }

        [Fact]
        public async Task SysCtl_ByComponent_ReturnsDriver()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockContainerDriver();
            await kernel.RegisterDriverAsync("test-driver", driver, new DriverContext("test-driver"));

            // Act
            var componentDriver = kernel.SysCtl("test-driver", DriverComponent.Container);

            // Assert
            Assert.NotNull(componentDriver);
            Assert.IsAssignableFrom<IContainerDriver>(componentDriver);
        }

        [Fact]
        public async Task DefaultDriverId_FirstRegistered_IsDefault()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockDriver();

            // Act
            await kernel.RegisterDriverAsync("first-driver", driver, new DriverContext("first-driver"));

            // Assert
            Assert.Equal("first-driver", kernel.DefaultDriverId);
        }

        [Fact]
        public async Task SetDefaultDriver_ChangesDefault()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver1 = new MockDriver();
            var driver2 = new MockDriver();
            await kernel.RegisterDriverAsync("driver-1", driver1, new DriverContext("driver-1"));
            await kernel.RegisterDriverAsync("driver-2", driver2, new DriverContext("driver-2"));

            // Act
            kernel.SetDefaultDriver("driver-2");

            // Assert
            Assert.Equal("driver-2", kernel.DefaultDriverId);
        }

        [Fact]
        public async Task Dispose_ClearsDrivers()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driver = new MockDriver();
            await kernel.RegisterDriverAsync("test-driver", driver, new DriverContext("test-driver"));

            // Act
            kernel.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => kernel.IsDriverRegistered("test-driver"));
        }

        [Fact]
        public void DisposedKernel_ThrowsException()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            kernel.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() =>
                kernel.GetDriver("any"));
        }

        [Fact]
        public void MultipleKernelInstances_AreIndependent()
        {
            // Arrange
            var kernel1 = new FluentDockerKernel();
            var kernel2 = new FluentDockerKernel();
            var driver1 = new MockDriver();
            var driver2 = new MockDriver();

            // Act
            kernel1.RegisterDriverAsync("driver-1", driver1, new DriverContext("driver-1")).Wait();
            kernel2.RegisterDriverAsync("driver-2", driver2, new DriverContext("driver-2")).Wait();

            // Assert
            Assert.True(kernel1.IsDriverRegistered("driver-1"));
            Assert.False(kernel1.IsDriverRegistered("driver-2"));
            Assert.True(kernel2.IsDriverRegistered("driver-2"));
            Assert.False(kernel2.IsDriverRegistered("driver-1"));

            // Cleanup
            kernel1.Dispose();
            kernel2.Dispose();
        }

        // Mock drivers for testing
        private class MockDriver : IDriver
        {
            public DriverType Type => DriverType.DockerCli;
            public RuntimeType Runtime => RuntimeType.Docker;

            public Task<DriverCapabilities> GetCapabilitiesAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(DriverCapabilities.Default());
            }

            public Task<bool> IsHealthyAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(true);
            }

            public Task InitializeAsync(DriverContext context, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        private class MockContainerDriver : MockDriver, IContainerDriver
        {
            public Task<CommandResponse<ContainerCreateResult>> CreateAsync(DriverContext context, ContainerCreateConfig config, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = "test-id" }));
            }

            public Task<CommandResponse<Unit>> StartAsync(DriverContext context, string containerId, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
            }

            public Task<CommandResponse<Unit>> StopAsync(DriverContext context, string containerId, int? timeout = null, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
            }

            public Task<CommandResponse<Unit>> RemoveAsync(DriverContext context, string containerId, bool force = false, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
            }

            public Task<CommandResponse<Model.Containers.Container>> InspectAsync(DriverContext context, string containerId, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<Model.Containers.Container>.Ok(new Model.Containers.Container()));
            }

            public Task<CommandResponse<System.Collections.Generic.IList<Model.Containers.Container>>> ListAsync(DriverContext context, ContainerListFilter filter = null, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<System.Collections.Generic.IList<Model.Containers.Container>>.Ok(new System.Collections.Generic.List<Model.Containers.Container>()));
            }

            public Task<CommandResponse<string>> GetLogsAsync(DriverContext context, string containerId, bool follow = false, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CommandResponse<string>.Ok("test logs"));
            }
        }
    }
}
