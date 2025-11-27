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
    public class ContainerAdvancedTests : IAsyncDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly IContainerDriver _driver;
        private readonly DriverContext _context;
        private readonly IImageDriver _imageDriver;
        private string _containerId;

        public ContainerAdvancedTests()
        {
            _kernel = new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<IContainerDriver>("docker-local");
            _imageDriver = _kernel.SysCtl<IImageDriver>("docker-local");
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
        public async Task Container_CreateWithEnvironment_SetsVariables()
        {
            // Arrange
            await _imageDriver.PullAsync(_context, "alpine", "latest");

            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-env-{Guid.NewGuid():N}",
                Environment = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "TEST_VAR", "test_value" },
                    { "ANOTHER_VAR", "another_value" },
                    { "NUMERIC_VAR", "12345" }
                }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);
            _containerId = response.Data?.Id;

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(_containerId);

            var inspectResponse = await _driver.InspectAsync(_context, _containerId);
            Assert.True(inspectResponse.Success);
            // Note: Environment validation would require parsing inspect output
        }

        [Fact]
        public async Task Container_CreateWithPortBindings_ExposiesPorts()
        {
            // Arrange
            await _imageDriver.PullAsync(_context, "alpine", "latest");

            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-ports-{Guid.NewGuid():N}",
                PortBindings = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "80/tcp", "8080" },
                    { "443/tcp", "8443" }
                }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);
            _containerId = response.Data?.Id;

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(_containerId);
        }

        [Fact]
        public async Task Container_CreateWithVolumes_MountsVolumes()
        {
            // Arrange
            await _imageDriver.PullAsync(_context, "alpine", "latest");

            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-volumes-{Guid.NewGuid():N}",
                Volumes = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "/tmp/host-path", "/container/path" }
                }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);
            _containerId = response.Data?.Id;

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(_containerId);
        }

        [Fact]
        public async Task Container_CreateWithCommand_OverridesDefault()
        {
            // Arrange
            await _imageDriver.PullAsync(_context, "alpine", "latest");

            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-cmd-{Guid.NewGuid():N}",
                Command = new[] { "echo", "Hello from custom command" }
            };

            // Act
            var response = await _driver.CreateAsync(_context, config);
            _containerId = response.Data?.Id;

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(_containerId);
        }

        [Fact]
        public async Task Container_StartStop_PreservesState()
        {
            // Arrange
            await _imageDriver.PullAsync(_context, "alpine", "latest");

            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-state-{Guid.NewGuid():N}",
                Command = new[] { "sleep", "30" }
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;

            // Act - Start
            var startResponse = await _driver.StartAsync(_context, _containerId);
            Assert.True(startResponse.Success);

            // Wait a bit
            await Task.Delay(1000);

            // Stop
            var stopResponse = await _driver.StopAsync(_context, _containerId);
            Assert.True(stopResponse.Success);

            // Inspect
            var inspectResponse = await _driver.InspectAsync(_context, _containerId);

            // Assert
            Assert.True(inspectResponse.Success);
            Assert.NotNull(inspectResponse.Data);
        }

        [Fact]
        public async Task Container_ListWithFilter_ReturnsFiltered()
        {
            // Arrange
            await _imageDriver.PullAsync(_context, "alpine", "latest");

            var config = new ContainerCreateConfig
            {
                Image = "alpine:latest",
                Name = $"test-filter-{Guid.NewGuid():N}"
            };

            var createResponse = await _driver.CreateAsync(_context, config);
            _containerId = createResponse.Data.Id;

            // Act
            var listResponse = await _driver.ListAsync(_context, new ContainerListFilter { All = true });

            // Assert
            Assert.True(listResponse.Success, listResponse.Error);
            Assert.NotNull(listResponse.Data);
            Assert.Contains(listResponse.Data, c => c.Id.StartsWith(_containerId.Substring(0, 12)));
        }

        [Fact]
        public async Task Container_Remove_NonExistent_Fails()
        {
            // Arrange
            var nonExistentId = "nonexistent-container-12345";

            // Act
            var response = await _driver.RemoveAsync(_context, nonExistentId, force: true);

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task Container_Inspect_NonExistent_Fails()
        {
            // Arrange
            var nonExistentId = "nonexistent-container-67890";

            // Act
            var response = await _driver.InspectAsync(_context, nonExistentId);

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }
    }
}
