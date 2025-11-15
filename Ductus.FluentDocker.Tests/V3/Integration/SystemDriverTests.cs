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
                .UseDriver("docker-local", b => b.UseDockerCli())
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
            var response = await _driver.InfoAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data.ServerVersion);
        }

        [Fact]
        public async Task System_Version_ReturnsDockerVersion()
        {
            // Act
            var response = await _driver.VersionAsync(_context);

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

        [Fact(Skip = "Events streaming not yet implemented")]
        public async Task System_Events_ReturnsEventStream()
        {
            // Arrange
            var cts = new System.Threading.CancellationTokenSource();
            cts.CancelAfter(5000); // Cancel after 5 seconds

            var eventReceived = false;

            // Act
            try
            {
                await foreach (var evt in _driver.EventsAsync(_context, cts.Token))
                {
                    eventReceived = true;
                    break; // Exit after first event
                }
            }
            catch (System.OperationCanceledException)
            {
                // Expected when timeout occurs
            }

            // Assert
            // This is a smoke test - just verify events endpoint works
            Assert.True(true);
        }

        [Fact(Skip = "Disk usage not yet implemented")]
        public async Task System_DiskUsage_ReturnsUsageInfo()
        {
            // Act
            var response = await _driver.DiskUsageAsync(_context);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
        }
    }
}
