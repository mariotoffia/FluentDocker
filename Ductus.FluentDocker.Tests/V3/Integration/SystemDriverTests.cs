using System.Threading.Tasks;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Drivers;
using Xunit;

namespace Ductus.FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class SystemDriverTests
    {
        private readonly FluentDockerKernel _kernel;
        private readonly ISystemDriver _driver;
        private readonly DriverContext _context;

        public SystemDriverTests()
        {
            _kernel = new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<ISystemDriver>("docker-local");
            _context = new DriverContext("docker-local");
        }

        [Fact]
        public async Task System_Info_ReturnsDockerInfo()
        {
            // Act
            var response = await _driver.GetInfoAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data.ServerVersion);
        }

        [Fact]
        public async Task System_Version_ReturnsDockerVersion()
        {
            // Act
            var response = await _driver.GetVersionAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data.Version);
        }

        [Fact]
        public async Task System_Ping_ReturnsSuccess()
        {
            // Act
            var response = await _driver.PingAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
        }

        // Note: EventsAsync and DiskUsageAsync are not implemented in ISystemDriver
        // These tests will be added when the methods are implemented
    }
}
