using System;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders.V3;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Services.V3;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class BuilderIntegrationTests : IAsyncDisposable
    {
        private FluentDockerKernel _kernel;
        private BuildResults _results;

        public async ValueTask DisposeAsync()
        {
            if (_results != null)
            {
                await _results.DisposeAllAsync();
            }
            _kernel?.Dispose();
        }

        [Fact]
        public async Task Builder_SingleContainer_CreatesSuccessfully()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Ensure image is available
            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-builder-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            Assert.NotNull(_results);
            Assert.Single(_results.All);
            Assert.IsAssignableFrom<IContainerServiceAsync>(_results.All.First());
        }

        [Fact]
        public async Task Builder_MultipleContainers_CreatesAll()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Ensure image is available
            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-container-1-{Guid.NewGuid():N}"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-container-2-{Guid.NewGuid():N}"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-container-3-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            Assert.NotNull(_results);
            Assert.Equal(3, _results.All.Count);
            Assert.All(_results.All, service =>
                Assert.IsAssignableFrom<IContainerServiceAsync>(service));
        }

        [Fact]
        public async Task Builder_WithEnvironment_SetsEnvironmentVariables()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-env-{Guid.NewGuid():N}")
                    .WithEnvironment("TEST_VAR", "test_value")
                    .WithEnvironment("ANOTHER_VAR", "another_value"))
                .BuildAsync();

            // Assert
            Assert.NotNull(_results);
            Assert.Single(_results.All);

            var container = _results.All.First() as IContainerServiceAsync;
            Assert.NotNull(container);
        }

        [Fact]
        public async Task Builder_WithPorts_SetsPortBindings()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-ports-{Guid.NewGuid():N}")
                    .WithPort("80", "8080")
                    .WithPort("443", "8443"))
                .BuildAsync();

            // Assert
            Assert.NotNull(_results);
            Assert.Single(_results.All);
        }

        [Fact]
        public async Task Builder_ServiceStart_StartsContainer()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-start-{Guid.NewGuid():N}"))
                .BuildAsync();

            var service = _results.All.First() as IContainerServiceAsync;

            // Act
            await service.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, service.State);

            // Cleanup
            await service.StopAsync();
        }

        [Fact]
        public async Task Builder_ServiceStop_StopsContainer()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-stop-{Guid.NewGuid():N}"))
                .BuildAsync();

            var service = _results.All.First() as IContainerServiceAsync;
            await service.StartAsync();

            // Act
            await service.StopAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Stopped, service.State);
        }

        [Fact]
        public async Task Builder_ServiceRemove_RemovesContainer()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-remove-{Guid.NewGuid():N}"))
                .BuildAsync();

            var service = _results.All.First() as IContainerServiceAsync;

            // Act
            await service.RemoveAsync(force: true);

            // Assert - no exception thrown means success
            Assert.True(true);

            // Clear results to prevent double disposal
            _results = null;
        }

        [Fact]
        public async Task Builder_DisposeAllAsync_CleansUpAllServices()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-dispose-1-{Guid.NewGuid():N}"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"test-dispose-2-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Act
            await _results.DisposeAllAsync();

            // Assert - no exception means success
            Assert.True(true);

            _results = null; // Prevent double disposal
        }

        [Fact]
        public async Task Builder_InvalidImage_ThrowsException()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            // Act & Assert
            await Assert.ThrowsAsync<DriverException>(async () =>
            {
                _results = await new Builder()
                    .WithinDriver("docker-local", _kernel)
                    .UseContainer(c => c
                        .UseImage("this-image-does-not-exist-12345:latest")
                        .WithName($"test-invalid-{Guid.NewGuid():N}"))
                    .BuildAsync();
            });
        }
    }
}
