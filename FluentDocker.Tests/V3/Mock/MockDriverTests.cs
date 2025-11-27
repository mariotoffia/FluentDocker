using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.V3.Mock
{
    [Trait("Category", "Mock")]
    public class MockDriverTests
    {
        [Fact]
        public void Constructor_DefaultParameters_CreatesDriver()
        {
            // Act
            var driver = new MockDriver();

            // Assert
            Assert.Equal(DriverType.DockerCli, driver.Type);
            Assert.Equal(RuntimeType.Docker, driver.Runtime);
        }

        [Fact]
        public void Constructor_CustomParameters_SetsProperties()
        {
            // Act
            var driver = new MockDriver(DriverType.PodmanCli, RuntimeType.Podman);

            // Assert
            Assert.Equal(DriverType.PodmanCli, driver.Type);
            Assert.Equal(RuntimeType.Podman, driver.Runtime);
        }

        [Fact]
        public async Task ContainerOperations_Success_ReturnsOk()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            var config = new ContainerCreateConfig { Image = "nginx" };

            // Act
            var createResponse = await driver.CreateAsync(context, config);
            var startResponse = await driver.StartAsync(context, createResponse.Data.Id);
            var stopResponse = await driver.StopAsync(context, createResponse.Data.Id);
            var removeResponse = await driver.RemoveAsync(context, createResponse.Data.Id, force: false);

            // Assert
            Assert.True(createResponse.Success);
            Assert.True(startResponse.Success);
            Assert.True(stopResponse.Success);
            Assert.True(removeResponse.Success);
        }

        [Fact]
        public async Task ContainerCreate_TracksContainer()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            var config = new ContainerCreateConfig { Image = "nginx", Name = "web" };

            // Act
            var createResponse = await driver.CreateAsync(context, config);
            var listResponse = await driver.ListAsync(context);

            // Assert
            Assert.Single(listResponse.Data);
            Assert.Equal(createResponse.Data.Id, listResponse.Data[0].Id);
        }

        [Fact]
        public async Task SimulateFailure_ReturnsFailure()
        {
            // Arrange
            var driver = new MockDriver
            {
                SimulateFailure = true,
                FailureMessage = "Test error",
                FailureErrorCode = ErrorCodes.Container.StartFailed
            };
            var context = new DriverContext("test");

            // Act
            var response = await driver.StartAsync(context, "any-id");

            // Assert
            Assert.False(response.Success);
            Assert.Equal("Test error", response.Error);
            Assert.Equal(ErrorCodes.Container.StartFailed, response.ErrorCode);
        }

        [Fact]
        public async Task MethodCalls_RecordsAllCalls()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");

            // Act
            await driver.IsHealthyAsync();
            await driver.GetCapabilitiesAsync();
            await driver.GetVersionAsync(context);

            // Assert
            Assert.Equal(3, driver.MethodCalls.Count);
            Assert.Contains(driver.MethodCalls, m => m.MethodName == nameof(driver.IsHealthyAsync));
            Assert.Contains(driver.MethodCalls, m => m.MethodName == nameof(driver.GetCapabilitiesAsync));
            Assert.Contains(driver.MethodCalls, m => m.MethodName == nameof(driver.GetVersionAsync));
        }

        [Fact]
        public async Task ImageOperations_Success_ReturnsOk()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");

            // Act
            var pullResponse = await driver.PullAsync(context, "nginx", "latest");
            var listResponse = await ((IImageDriver)driver).ListAsync(context, null);

            // Assert
            Assert.True(pullResponse.Success);
            Assert.True(listResponse.Success);
            Assert.Single(listResponse.Data);
        }

        [Fact]
        public async Task NetworkOperations_Success_ReturnsOk()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            var config = new NetworkCreateConfig { Name = "mynet" };

            // Act
            var createResponse = await driver.CreateAsync(context, config);
            var listResponse = await ((INetworkDriver)driver).ListAsync(context, null);
            var removeResponse = await ((INetworkDriver)driver).RemoveAsync(context, createResponse.Data.Id);

            // Assert
            Assert.True(createResponse.Success);
            Assert.True(listResponse.Success);
            Assert.Single(listResponse.Data);
            Assert.True(removeResponse.Success);
        }

        [Fact]
        public async Task VolumeOperations_Success_ReturnsOk()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            var config = new VolumeCreateConfig { Name = "myvol" };

            // Act
            var createResponse = await driver.CreateAsync(context, config);
            var listResponse = await ((IVolumeDriver)driver).ListAsync(context, null);

            // Assert
            Assert.True(createResponse.Success);
            Assert.Equal("myvol", createResponse.Data.Name);
            Assert.True(listResponse.Success);
            Assert.Single(listResponse.Data);
        }

        [Fact]
        public async Task SystemOperations_Success_ReturnsOk()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");

            // Act
            var pingResponse = await driver.PingAsync(context);
            var infoResponse = await driver.GetInfoAsync(context);
            var versionResponse = await driver.GetVersionAsync(context);

            // Assert
            Assert.True(pingResponse.Success);
            Assert.True(infoResponse.Success);
            Assert.True(versionResponse.Success);
            Assert.Equal("20.10.0", versionResponse.Data.Version);
        }

        [Fact]
        public async Task ComposeOperations_Success_ReturnsOk()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            var upConfig = new ComposeUpConfig();
            var downConfig = new ComposeDownConfig();

            // Act
            var upResponse = await driver.UpAsync(context, upConfig);
            var downResponse = await driver.DownAsync(context, downConfig);

            // Assert
            Assert.True(upResponse.Success);
            Assert.True(downResponse.Success);
        }

        [Fact]
        public void ClearMethodCalls_ClearsHistory()
        {
            // Arrange
            var driver = new MockDriver();
            driver.IsHealthyAsync().Wait();
            driver.IsHealthyAsync().Wait();

            // Act
            driver.ClearMethodCalls();

            // Assert
            Assert.Empty(driver.MethodCalls);
        }

        [Fact]
        public async Task ClearAll_ClearsEverything()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            await driver.CreateAsync(context, new ContainerCreateConfig { Image = "nginx" });
            await driver.PullAsync(context, "nginx", "latest");

            // Act
            driver.ClearAll();

            // Assert
            Assert.Empty(driver.MethodCalls);
            var containers = await driver.ListAsync(context);
            var images = await ((IImageDriver)driver).ListAsync(context, null);
            Assert.Empty(containers.Data);
            Assert.Empty(images.Data);
        }

        [Fact]
        public async Task Container_StateTransitions()
        {
            // Arrange
            var driver = new MockDriver();
            var context = new DriverContext("test");
            var config = new ContainerCreateConfig { Image = "nginx" };

            // Act
            var createResponse = await driver.CreateAsync(context, config);
            var inspectAfterCreate = await driver.InspectAsync(context, createResponse.Data.Id);

            await driver.StartAsync(context, createResponse.Data.Id);
            var inspectAfterStart = await driver.InspectAsync(context, createResponse.Data.Id);

            await driver.StopAsync(context, createResponse.Data.Id);
            var inspectAfterStop = await driver.InspectAsync(context, createResponse.Data.Id);

            // Assert
            Assert.Equal("created", inspectAfterCreate.Data.State?.Status);
            Assert.Equal("running", inspectAfterStart.Data.State?.Status);
            Assert.Equal("exited", inspectAfterStop.Data.State?.Status);
        }

        [Fact]
        public async Task IsHealthy_WithSimulateFailure_ReturnsFalse()
        {
            // Arrange
            var driver = new MockDriver { SimulateFailure = true };

            // Act
            var healthy = await driver.IsHealthyAsync();

            // Assert
            Assert.False(healthy);
        }

        [Fact]
        public async Task GetCapabilities_ReturnsDefault()
        {
            // Arrange
            var driver = new MockDriver();

            // Act
            var capabilities = await driver.GetCapabilitiesAsync();

            // Assert
            Assert.True(capabilities.SupportsContainers);
            Assert.True(capabilities.SupportsImages);
            Assert.True(capabilities.SupportsNetworks);
            Assert.True(capabilities.SupportsVolumes);
        }
    }
}
