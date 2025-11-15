using System;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Drivers;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class ContainerLifecycleTests : IAsyncDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly IContainerDriver _driver;
        private readonly DriverContext _context;
        private string _containerId;

        public ContainerLifecycleTests()
        {
            _kernel = new KernelBuilder()
                .UseDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<IContainerDriver>("docker-local");
            _context = new DriverContext("docker-local");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_containerId))
                {
                    await _driver.RemoveAsync(_context, _containerId, force: true);
                }
            }
            catch { /* Ignore cleanup errors */ }

            _kernel?.Dispose();
        }

        [Fact]
        public async Task CreateContainer_ValidConfig_ReturnsContainerId()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}"
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);
            _containerId = response.Data?.Id;

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(_containerId);
            Assert.NotEmpty(_containerId);
        }

        [Fact]
        public async Task StartContainer_CreatedContainer_Starts()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}",
                Command = new[] { "sleep", "30" }
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;
            Assert.True(createResponse.Success);

            // Act
            var startResponse = await _driver.StartAsync(_context, _containerId);

            // Assert
            Assert.True(startResponse.Success, startResponse.Error);

            // Verify running
            var inspectResponse = await _driver.InspectAsync(_context, _containerId);
            Assert.True(inspectResponse.Success);
            Assert.NotNull(inspectResponse.Data);
        }

        [Fact]
        public async Task StopContainer_RunningContainer_Stops()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}",
                Command = new[] { "sleep", "30" }
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;
            await _driver.StartAsync(_context, _containerId);

            // Act
            var stopResponse = await _driver.StopAsync(_context, _containerId, timeout: 10);

            // Assert
            Assert.True(stopResponse.Success, stopResponse.Error);
        }

        [Fact]
        public async Task RemoveContainer_StoppedContainer_Removes()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}"
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            var containerId = createResponse.Data.Id;

            // Act
            var removeResponse = await _driver.RemoveAsync(_context, containerId, force: true);

            // Assert
            Assert.True(removeResponse.Success, removeResponse.Error);

            // Clear ID so disposal doesn't try to remove again
            _containerId = null;
        }

        [Fact]
        public async Task InspectContainer_ExistingContainer_ReturnsDetails()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}"
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;

            // Act
            var inspectResponse = await _driver.InspectAsync(_context, _containerId);

            // Assert
            Assert.True(inspectResponse.Success, inspectResponse.Error);
            Assert.NotNull(inspectResponse.Data);
            Assert.Equal(_containerId, inspectResponse.Data.Id);
        }

        [Fact]
        public async Task ListContainers_WithContainers_ReturnsAll()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}"
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;

            // Act
            var listResponse = await _driver.ListAsync(_context, all: true);

            // Assert
            Assert.True(listResponse.Success, listResponse.Error);
            Assert.NotNull(listResponse.Data);
            Assert.Contains(listResponse.Data, c => c.Id.StartsWith(_containerId.Substring(0, 12)));
        }

        [Fact]
        public async Task GetLogs_Container_ReturnsLogs()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}",
                Command = new[] { "echo", "Hello from container" }
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;
            await _driver.StartAsync(_context, _containerId);

            // Wait a bit for container to execute
            await Task.Delay(1000);

            // Act
            var logsResponse = await _driver.GetLogsAsync(_context, _containerId);

            // Assert
            Assert.True(logsResponse.Success, logsResponse.Error);
            Assert.NotNull(logsResponse.Data);
        }

        [Fact]
        public async Task FullLifecycle_CreateStartStopRemove_Success()
        {
            // Arrange
            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-container-{Guid.NewGuid():N}",
                Command = new[] { "sleep", "30" }
            };

            // Act & Assert - Create
            var createResponse = await _driver.CreateAsync(_context, config);
            Assert.True(createResponse.Success, createResponse.Error);
            _containerId = createResponse.Data.Id;

            // Start
            var startResponse = await _driver.StartAsync(_context, _containerId);
            Assert.True(startResponse.Success, startResponse.Error);

            // Stop
            var stopResponse = await _driver.StopAsync(_context, _containerId, timeout: 10);
            Assert.True(stopResponse.Success, stopResponse.Error);

            // Remove
            var removeResponse = await _driver.RemoveAsync(_context, _containerId, force: true);
            Assert.True(removeResponse.Success, removeResponse.Error);

            _containerId = null; // Cleanup done
        }
    }
}
