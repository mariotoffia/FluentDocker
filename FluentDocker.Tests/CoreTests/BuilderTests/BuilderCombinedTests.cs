using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
    /// <summary>
    /// Unit tests for Builder combined resource operations.
    /// Tests scenarios with multiple resource types.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BuilderCombinedTests : MockKernelTestBase, IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            await InitializeMockKernelAsync();
        }

        public Task DisposeAsync()
        {
            return base.DisposeAsync().AsTask();
        }

        [Fact]
        public async Task UseNetworkAndContainer_CreatesNetworkFirst()
        {
            // Arrange
            MockPack
                .SetupNetworkList()
                .SetupNetworkCreate("network-123")
                .SetupNetworkRemove()
                .SetupContainerCreate("container-123")
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName("app-network")
                    .WithSubnet("172.20.0.0/16"))
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .WithName("web")
                    .WithNetwork("app-network"))
                .BuildAsync();

            // Assert
            Assert.Equal(2, results.All.Count());
            Assert.Single(results.All.OfType<INetworkService>());
            Assert.Single(results.All.OfType<IContainerService>());

            // Verify order - network created first
            var networkCallOrder = 0;
            var containerCallOrder = 0;
            var callCounter = 0;

            MockPack.NetworkDriver
                .Setup(d => d.CreateAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.IsAny<NetworkCreateConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Callback(() => networkCallOrder = ++callCounter);

            MockPack.ContainerDriver
                .Setup(d => d.CreateAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.IsAny<ContainerCreateConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Callback(() => containerCallOrder = ++callCounter);
        }

        [Fact]
        public async Task UseVolumeAndContainer_CreatesVolumeFirst()
        {
            // Arrange
            MockPack
                .SetupVolumeCreate("data-volume")
                .SetupVolumeRemove()
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseVolume(v => v.WithName("data-volume"))
                .UseContainer(c => c
                    .UseImage("postgres:13")
                    .WithVolume("data-volume", "/var/lib/postgresql/data"))
                .BuildAsync();

            // Assert
            Assert.Equal(2, results.All.Count());
            Assert.Single(results.All.OfType<IVolumeService>());
            Assert.Single(results.All.OfType<IContainerService>());
        }

        [Fact]
        public async Task FullStack_NetworkVolumeContainer_CreatesAll()
        {
            // Arrange
            MockPack
                .SetupNetworkList()
                .SetupNetworkCreate()
                .SetupNetworkRemove()
                .SetupVolumeCreate()
                .SetupVolumeRemove()
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n
                    .WithName("app-network")
                    .UseDriver("bridge")
                    .WithLabel("env", "test"))
                .UseVolume(v => v
                    .WithName("postgres-data")
                    .WithLabel("backup", "true"))
                .UseContainer(c => c
                    .UseImage("postgres:13")
                    .WithName("db")
                    .WithNetwork("app-network")
                    .WithVolume("postgres-data", "/var/lib/postgresql/data")
                    .WithEnvironment("POSTGRES_PASSWORD", "secret"))
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .WithName("web")
                    .WithNetwork("app-network")
                    .WithPort("80/tcp", "8080"))
                .BuildAsync();

            // Assert
            Assert.Equal(4, results.All.Count());
            Assert.Single(results.All.OfType<INetworkService>());
            Assert.Single(results.All.OfType<IVolumeService>());
            Assert.Equal(2, results.All.OfType<IContainerService>().Count());
        }

        [Fact]
        public async Task DisposeAllAsync_DisposesAllResources()
        {
            // Arrange
            MockPack
                .SetupNetworkList()
                .SetupNetworkCreate()
                .SetupNetworkRemove()
                .SetupVolumeCreate()
                .SetupVolumeRemove()
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n.WithName("net").RemoveOnDispose())
                .UseVolume(v => v.WithName("vol").RemoveOnDispose())
                .UseContainer(c => c.UseImage("alpine"))
                .BuildAsync();

            // Act
            await results.DisposeAllAsync();

            // Assert - All resources should be disposed
            MockPack.VerifyContainerStopped(Times.Once());
            MockPack.VerifyContainerRemoved(Times.Once());
            MockPack.NetworkDriver.Verify(d => d.RemoveAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BuildResults_ForDriver_FiltersCorrectly()
        {
            // Arrange
            MockPack
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c.UseImage("nginx"))
                .BuildAsync();

            // Assert
            var dockerResults = results.ForDriver(DriverId);
            Assert.Single(dockerResults);
        }

        [Fact]
        public async Task Builder_WithoutKernel_ThrowsException()
        {
            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await new Builder()
                    .UseContainer(c => c.UseImage("nginx"))
                    .BuildAsync();
            });

            Assert.Contains("WithinDriver", ex.Message);
        }

        [Fact]
        public async Task BuildSync_WorksCorrectly()
        {
            // Arrange
            MockPack
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act - Use sync Build() method
            var results = new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c.UseImage("nginx"))
                .Build();

            // Assert
            Assert.NotNull(results);
            Assert.Single(results.All);
        }

        [Fact]
        public async Task MultipleContainersWithLinks_CreatesInOrder()
        {
            // Arrange
            MockPack
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act
            var results = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("redis:alpine")
                    .WithName("cache"))
                .UseContainer(c => c
                    .UseImage("postgres:13")
                    .WithName("db"))
                .UseContainer(c => c
                    .UseImage("myapp:latest")
                    .WithName("app")
                    .WithLink("cache")
                    .WithLink("db"))
                .BuildAsync();

            // Assert
            Assert.Equal(3, results.All.Count());
        }

        [Fact]
        public async Task ContainerWithNetworkAlias_PassesAliasConfig()
        {
            // Arrange
            MockPack
                .SetupNetworkList()
                .SetupNetworkCreate()
                .SetupContainerCreate()
                .SetupContainerStart()
                .SetupContainerInspect(running: true)
                .SetupContainerStop()
                .SetupContainerRemove();

            // Act
            await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseNetwork(n => n.WithName("app-net"))
                .UseContainer(c => c
                    .UseImage("nginx")
                    .WithNetworkAlias("app-net", "web-service"))
                .BuildAsync();

            // Assert - Verify container was created with network
            MockPack.ContainerDriver.Verify(d => d.CreateAsync(
                It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                It.Is<ContainerCreateConfig>(cfg =>
                    cfg.Networks != null &&
                    cfg.Networks.Contains("app-net")),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
    }
}
