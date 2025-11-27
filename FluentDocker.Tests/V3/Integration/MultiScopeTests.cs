using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders.V3;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using FluentDocker.Services.V3;
using Xunit;

namespace FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class MultiScopeTests : IAsyncDisposable
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
        public async Task Builder_MultipleDriverScopes_CreatesInEach()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-1", b => b.UseDockerCli())
                .WithDriver("docker-2", b => b.UseDockerCli())
                .BuildAsync();

            // Ensure images are available
            var driver1 = _kernel.SysCtl<Drivers.IImageDriver>("docker-1");
            var driver2 = _kernel.SysCtl<Drivers.IImageDriver>("docker-2");
            await driver1.PullAsync(new Model.Drivers.DriverContext("docker-1"), "alpine", "latest");
            await driver2.PullAsync(new Model.Drivers.DriverContext("docker-2"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-1", _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"scope1-container-{Guid.NewGuid():N}"))
                .WithinDriver("docker-2")
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName($"scope2-container-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            Assert.NotNull(_results);
            Assert.Equal(2, _results.All.Count);
            Assert.Equal(2, _results.Scopes.Count);

            var scope1Services = _results.ForDriver("docker-1");
            var scope2Services = _results.ForDriver("docker-2");

            Assert.Single(scope1Services);
            Assert.Single(scope2Services);
        }

        [Fact]
        public async Task Builder_MultipleContainersPerScope_AllCreated()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-1", b => b.UseDockerCli())
                .WithDriver("docker-2", b => b.UseDockerCli())
                .BuildAsync();

            var driver1 = _kernel.SysCtl<Drivers.IImageDriver>("docker-1");
            var driver2 = _kernel.SysCtl<Drivers.IImageDriver>("docker-2");
            await driver1.PullAsync(new Model.Drivers.DriverContext("docker-1"), "alpine", "latest");
            await driver2.PullAsync(new Model.Drivers.DriverContext("docker-2"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-1", _kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-c1-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-c2-{Guid.NewGuid():N}"))
                .WithinDriver("docker-2")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-c1-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-c2-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-c3-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            Assert.Equal(5, _results.All.Count);
            Assert.Equal(2, _results.Scopes.Count);

            var scope1Services = _results.ForDriver("docker-1");
            var scope2Services = _results.ForDriver("docker-2");

            Assert.Equal(2, scope1Services.Count);
            Assert.Equal(3, scope2Services.Count);
        }

        [Fact]
        public async Task Builder_SwitchingBetweenScopes_MaintainsScope()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-1", b => b.UseDockerCli())
                .WithDriver("docker-2", b => b.UseDockerCli())
                .BuildAsync();

            var driver1 = _kernel.SysCtl<Drivers.IImageDriver>("docker-1");
            var driver2 = _kernel.SysCtl<Drivers.IImageDriver>("docker-2");
            await driver1.PullAsync(new Model.Drivers.DriverContext("docker-1"), "alpine", "latest");
            await driver2.PullAsync(new Model.Drivers.DriverContext("docker-2"), "alpine", "latest");

            // Act - Switch between scopes multiple times
            _results = await new Builder()
                .WithinDriver("docker-1", _kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-first-{Guid.NewGuid():N}"))
                .WithinDriver("docker-2")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-first-{Guid.NewGuid():N}"))
                .WithinDriver("docker-1")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-second-{Guid.NewGuid():N}"))
                .WithinDriver("docker-2")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-second-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            Assert.Equal(4, _results.All.Count);

            var scope1Services = _results.ForDriver("docker-1");
            var scope2Services = _results.ForDriver("docker-2");

            Assert.Equal(2, scope1Services.Count);
            Assert.Equal(2, scope2Services.Count);
        }

        [Fact]
        public async Task Builder_KernelReuse_WorksCorrectly()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync();

            var driver = _kernel.SysCtl<Drivers.IImageDriver>("docker-local");
            await driver.PullAsync(new Model.Drivers.DriverContext("docker-local"), "alpine", "latest");

            // Act - First build
            var results1 = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"first-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Second build with same kernel
            _results = await new Builder()
                .WithinDriver("docker-local", _kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"second-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            Assert.Single(results1.All);
            Assert.Single(_results.All);

            // Cleanup first results
            await results1.DisposeAllAsync();
        }

        [Fact]
        public async Task Builder_ForDriver_FiltersCorrectly()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-1", b => b.UseDockerCli())
                .WithDriver("docker-2", b => b.UseDockerCli())
                .WithDriver("docker-3", b => b.UseDockerCli())
                .BuildAsync();

            var driver1 = _kernel.SysCtl<Drivers.IImageDriver>("docker-1");
            var driver2 = _kernel.SysCtl<Drivers.IImageDriver>("docker-2");
            var driver3 = _kernel.SysCtl<Drivers.IImageDriver>("docker-3");
            await driver1.PullAsync(new Model.Drivers.DriverContext("docker-1"), "alpine", "latest");
            await driver2.PullAsync(new Model.Drivers.DriverContext("docker-2"), "alpine", "latest");
            await driver3.PullAsync(new Model.Drivers.DriverContext("docker-3"), "alpine", "latest");

            _results = await new Builder()
                .WithinDriver("docker-1", _kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-{Guid.NewGuid():N}"))
                .WithinDriver("docker-2")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-c1-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-c2-{Guid.NewGuid():N}"))
                .WithinDriver("docker-3")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d3-c1-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d3-c2-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d3-c3-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Act & Assert
            Assert.Single(_results.ForDriver("docker-1"));
            Assert.Equal(2, _results.ForDriver("docker-2").Count);
            Assert.Equal(3, _results.ForDriver("docker-3").Count);
            Assert.Empty(_results.ForDriver("non-existent"));
        }

        [Fact]
        public async Task Builder_AllServices_FromAllScopes()
        {
            // Arrange
            _kernel = await new KernelBuilder()
                .WithDriver("docker-1", b => b.UseDockerCli())
                .WithDriver("docker-2", b => b.UseDockerCli())
                .BuildAsync();

            var driver1 = _kernel.SysCtl<Drivers.IImageDriver>("docker-1");
            var driver2 = _kernel.SysCtl<Drivers.IImageDriver>("docker-2");
            await driver1.PullAsync(new Model.Drivers.DriverContext("docker-1"), "alpine", "latest");
            await driver2.PullAsync(new Model.Drivers.DriverContext("docker-2"), "alpine", "latest");

            // Act
            _results = await new Builder()
                .WithinDriver("docker-1", _kernel)
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-{Guid.NewGuid():N}"))
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d1-{Guid.NewGuid():N}"))
                .WithinDriver("docker-2")
                .UseContainer(c => c.UseImage("alpine:latest").WithName($"d2-{Guid.NewGuid():N}"))
                .BuildAsync();

            // Assert
            var allServices = _results.All;
            Assert.Equal(3, allServices.Count);
            Assert.All(allServices, service =>
                Assert.IsAssignableFrom<IContainerServiceAsync>(service));
        }
    }
}
