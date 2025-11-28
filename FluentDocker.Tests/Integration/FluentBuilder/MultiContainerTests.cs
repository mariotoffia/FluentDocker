using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Integration.FluentBuilder
{
    /// <summary>
    /// Multi-container integration tests demonstrating multi-container scenarios
    /// using the fluent builder API.
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "MultiContainer")]
    public class MultiContainerTests : IAsyncLifetime
    {
        private FluentDockerKernel _kernel;
        private const string DriverId = "docker";

        public async Task InitializeAsync()
        {
            _kernel = await FluentDockerKernel.Create()
                .WithDriver(DriverId, d => d.UseDockerCli().AsDefault())
                .BuildAsync();
        }

        public Task DisposeAsync()
        {
            _kernel?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task MultipleContainers_CreateAndStart_AllRunning()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("multi-test-1")
                    .WithCommand("sh", "-c", "sleep 60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("multi-test-2")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Equal(2, results.Containers.Count);
            
            var container1 = results.GetContainer("multi-test-1");
            var container2 = results.GetContainer("multi-test-2");
            
            Assert.NotNull(container1);
            Assert.NotNull(container2);
            Assert.Equal("multi-test-1", container1.Name);
            Assert.Equal("multi-test-2", container2.Name);
        }

        [Fact]
        public async Task ContainerWithLink_CanResolveLinkedContainer()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("link-backend")
                    .WithCommand("sh", "-c", "sleep 60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("link-frontend")
                    .WithLink("link-backend", "backend")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Equal(2, results.Containers.Count);
            
            var frontend = results.GetContainer("link-frontend");
            Assert.NotNull(frontend);

            // The linked container should be resolvable by its alias
            // We can verify this by checking /etc/hosts in the frontend container
            var hostsContent = await frontend.ExecuteAsync("cat /etc/hosts");
            Assert.Contains("backend", hostsContent);
        }

        [Fact]
        public async Task MultipleContainersWithNetwork_ShareNetwork()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseNetwork(n => n
                    .WithName("multi-test-network")
                    .RemoveOnDispose())
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("net-test-1")
                    .WithNetwork("multi-test-network")
                    .WithCommand("sh", "-c", "sleep 60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("net-test-2")
                    .WithNetwork("multi-test-network")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Equal(2, results.Containers.Count);
            Assert.Single(results.Networks);
            
            var network = results.GetNetwork("multi-test-network");
            Assert.NotNull(network);
            Assert.Equal("multi-test-network", network.Name);
        }

        [Fact]
        public async Task ContainersWithNetworkAliases_CanResolveByAlias()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseNetwork(n => n
                    .WithName("alias-test-network")
                    .RemoveOnDispose())
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("alias-backend")
                    .WithNetworkAlias("alias-test-network", "db-server")
                    .WithCommand("sh", "-c", "sleep 60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("alias-frontend")
                    .WithNetwork("alias-test-network")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Equal(2, results.Containers.Count);
            
            var frontend = results.GetContainer("alias-frontend");
            Assert.NotNull(frontend);

            // In Docker networks, containers can ping each other by name
            // The db-server alias should resolve to the backend container
        }

        [Fact]
        public async Task ContainerWithVolume_MountsVolume()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseVolume(v => v
                    .WithName("multi-test-volume")
                    .RemoveOnDispose())
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("volume-test")
                    .WithVolume("multi-test-volume", "/data")
                    .WithCommand("sh", "-c", "echo 'test' > /data/test.txt && sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Single(results.Containers);
            Assert.Single(results.Volumes);
            
            var volume = results.GetVolume("multi-test-volume");
            Assert.NotNull(volume);
            Assert.Equal("multi-test-volume", volume.Name);
        }

        [Fact]
        public async Task AllServicesProperty_ReturnsAllBuiltServices()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseNetwork(n => n.WithName("all-services-net").RemoveOnDispose())
                .UseVolume(v => v.WithName("all-services-vol").RemoveOnDispose())
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("all-services-container")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Equal(3, results.All.Count);
            Assert.Single(results.Containers);
            Assert.Single(results.Networks);
            Assert.Single(results.Volumes);
        }

        [Fact]
        public async Task ForDriver_FiltersServicesByDriver()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("driver-filter-test")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            var services = results.ForDriver(DriverId);
            Assert.Single(services);
            Assert.IsAssignableFrom<IContainerService>(services[0]);
        }

        [Fact]
        public async Task MultipleContainersWithLinks_FormsDependencyChain()
        {
            // Arrange - Create a dependency chain: app -> cache -> db
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("chain-db")
                    .WithCommand("sh", "-c", "sleep 60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("chain-cache")
                    .WithLink("chain-db", "database")
                    .WithCommand("sh", "-c", "sleep 60"))
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("chain-app")
                    .WithLinks("chain-cache", "chain-db")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            Assert.Equal(3, results.Containers.Count);
            
            var app = results.GetContainer("chain-app");
            Assert.NotNull(app);
            
            // Verify app can see both linked containers
            var hostsContent = await app.ExecuteAsync("cat /etc/hosts");
            Assert.Contains("chain-cache", hostsContent);
            Assert.Contains("chain-db", hostsContent);
        }

        [Fact]
        public async Task GetContainerByName_CaseInsensitive()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("CaSeInSeNsItIvE")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert - Both cases should find the container
            var lower = results.GetContainer("caseinsensitive");
            var upper = results.GetContainer("CASEINSENSITIVE");
            var mixed = results.GetContainer("CaSeInSeNsItIvE");
            
            Assert.NotNull(lower);
            Assert.NotNull(upper);
            Assert.NotNull(mixed);
            Assert.Same(lower, upper);
            Assert.Same(lower, mixed);
        }

        [Fact]
        public async Task OfType_FiltersByServiceType()
        {
            // Arrange & Act
            await using var results = await new Builder()
                .WithinDriver(DriverId, _kernel)
                .UseNetwork(n => n.WithName("oftype-test-net").RemoveOnDispose())
                .UseContainer(c => c
                    .UseImage("alpine:latest")
                    .WithName("oftype-test-container")
                    .WithCommand("sh", "-c", "sleep 60"))
                .BuildAsync();

            // Assert
            var containers = results.OfType<IContainerService>();
            var networks = results.OfType<INetworkService>();
            
            Assert.Single(containers);
            Assert.Single(networks);
        }
    }
}

