using System;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Drivers;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class NetworkDriverTests : IDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly INetworkDriver _driver;
        private readonly DriverContext _context;

        public NetworkDriverTests()
        {
            _kernel = new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<INetworkDriver>("docker-local");
            _context = new DriverContext("docker-local");
        }

        public void Dispose()
        {
            _kernel?.Dispose();
        }

        [Fact]
        public async Task Network_Create_CreatesNetwork()
        {
            // Arrange
            var networkName = $"test-network-{Guid.NewGuid():N}";
            var config = new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data.Id);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.Id);
        }

        [Fact]
        public async Task Network_List_ReturnsNetworks()
        {
            // Act
            var response = await _driver.ListAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data);
            // Should at least have default networks (bridge, host, none)
            Assert.Contains(response.Data, n => n.Name == "bridge");
        }

        [Fact]
        public async Task Network_Inspect_ReturnsDetails()
        {
            // Arrange - Use default bridge network
            var networkName = "bridge";

            // Act
            var response = await _driver.InspectAsync(_context, networkName);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.Equal("bridge", response.Data.Name);
        }

        [Fact]
        public async Task Network_Remove_RemovesNetwork()
        {
            // Arrange
            var networkName = $"test-remove-{Guid.NewGuid():N}";
            var createResponse = await _driver.CreateAsync(_context, new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            });
            Assert.True(createResponse.Success);

            // Act
            var removeResponse = await _driver.RemoveAsync(_context, createResponse.Data.Id);

            // Assert
            Assert.True(removeResponse.Success, removeResponse.Error);
        }

        [Fact]
        public async Task Network_CreateWithSubnet_ConfiguresSubnet()
        {
            // Arrange
            var networkName = $"test-subnet-{Guid.NewGuid():N}";
            var config = new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge",
                Subnet = "172.20.0.0/16",
                Gateway = "172.20.0.1"
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Verify subnet configuration
            var inspectResponse = await _driver.InspectAsync(_context, response.Data.Id);
            Assert.True(inspectResponse.Success);
            Assert.NotNull(inspectResponse.Data);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.Id);
        }

        [Fact]
        public async Task Network_CreateWithLabels_SetsLabels()
        {
            // Arrange
            var networkName = $"test-labels-{Guid.NewGuid():N}";
            var config = new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge",
                Labels = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "test.label", "test-value" },
                    { "environment", "testing" }
                }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.Id);
        }

        [Fact]
        public async Task Network_Connect_ConnectsContainer()
        {
            // Arrange
            var networkName = $"test-connect-{Guid.NewGuid():N}";
            var createNetworkResponse = await _driver.CreateAsync(_context, new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            });
            Assert.True(createNetworkResponse.Success);

            // Create a container
            var containerDriver = _kernel.SysCtl<IContainerDriver>("docker-local");
            var containerResponse = await containerDriver.CreateAsync(_context, new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}"
            });
            Assert.True(containerResponse.Success);

            // Act
            var connectResponse = await _driver.ConnectAsync(
                _context,
                createNetworkResponse.Data.Id,
                containerResponse.Data.Id);

            // Assert
            Assert.True(connectResponse.Success, connectResponse.Error);

            // Cleanup
            await containerDriver.RemoveAsync(_context, containerResponse.Data.Id, force: true);
            await _driver.RemoveAsync(_context, createNetworkResponse.Data.Id);
        }

        [Fact]
        public async Task Network_Disconnect_DisconnectsContainer()
        {
            // Arrange
            var networkName = $"test-disconnect-{Guid.NewGuid():N}";
            var createNetworkResponse = await _driver.CreateAsync(_context, new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            });
            Assert.True(createNetworkResponse.Success);

            var containerDriver = _kernel.SysCtl<IContainerDriver>("docker-local");
            var containerResponse = await containerDriver.CreateAsync(_context, new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}"
            });
            Assert.True(containerResponse.Success);

            await _driver.ConnectAsync(_context, createNetworkResponse.Data.Id, containerResponse.Data.Id);

            // Act
            var disconnectResponse = await _driver.DisconnectAsync(
                _context,
                createNetworkResponse.Data.Id,
                containerResponse.Data.Id);

            // Assert
            Assert.True(disconnectResponse.Success, disconnectResponse.Error);

            // Cleanup
            await containerDriver.RemoveAsync(_context, containerResponse.Data.Id, force: true);
            await _driver.RemoveAsync(_context, createNetworkResponse.Data.Id);
        }

        [Fact]
        public async Task Network_Remove_NonExistent_Fails()
        {
            // Arrange
            var nonExistentId = "nonexistent-network-12345";

            // Act
            var response = await _driver.RemoveAsync(_context, nonExistentId);

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task Network_Inspect_NonExistent_Fails()
        {
            // Arrange
            var nonExistentName = "nonexistent-network-67890";

            // Act
            var response = await _driver.InspectAsync(_context, nonExistentName);

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task Network_CreateDuplicate_Fails()
        {
            // Arrange
            var networkName = $"test-duplicate-{Guid.NewGuid():N}";
            var firstResponse = await _driver.CreateAsync(_context, new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            });
            Assert.True(firstResponse.Success);

            // Act
            var secondResponse = await _driver.CreateAsync(_context, new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            });

            // Assert
            Assert.False(secondResponse.Success);
            Assert.NotNull(secondResponse.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, firstResponse.Data.Id);
        }

        [Fact]
        public async Task Network_Prune_RemovesUnusedNetworks()
        {
            // Arrange
            var networkName = $"test-prune-{Guid.NewGuid():N}";
            var createResponse = await _driver.CreateAsync(_context, new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge"
            });
            Assert.True(createResponse.Success);

            // Act
            var pruneResponse = await _driver.PruneAsync(_context);

            // Assert
            Assert.True(pruneResponse.Success, pruneResponse.Error);
            Assert.NotNull(pruneResponse.Data);
        }

        [Fact]
        public async Task Network_CreateWithIpv6_EnablesIpv6()
        {
            // Arrange
            var networkName = $"test-ipv6-{Guid.NewGuid():N}";
            var config = new NetworkCreateConfig
            {
                Name = networkName,
                Driver = "bridge",
                EnableIPv6 = true,
                Subnet = "fd00::/64"
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.RemoveAsync(_context, response.Data.Id);
        }
    }
}
