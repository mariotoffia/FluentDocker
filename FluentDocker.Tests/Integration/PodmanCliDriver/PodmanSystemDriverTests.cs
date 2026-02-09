using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
    /// <summary>
    /// Integration tests for Podman system driver.
    /// Requires Podman to be installed.
    /// </summary>
    [Collection("PodmanDriver")]
    [Trait("Category", "PodmanIntegration")]
    public class PodmanSystemDriverTests : PodmanDriverTestBase
    {
        [Fact]
        public async Task Ping_ReturnsSuccess()
        {
            var result = await SystemDriver.PingAsync(Context);
            Assert.True(result.Success, $"Ping failed: {result.Error}");
        }

        [Fact]
        public async Task GetInfo_ReturnsSystemInfo()
        {
            var result = await SystemDriver.GetInfoAsync(Context);
            Assert.True(result.Success, $"GetInfo failed: {result.Error}");
            Assert.NotNull(result.Data);
            Assert.Equal("linux", result.Data.OSType);
        }

        [Fact]
        public async Task GetVersion_ReturnsVersionInfo()
        {
            var result = await SystemDriver.GetVersionAsync(Context);
            Assert.True(result.Success, $"GetVersion failed: {result.Error}");
            Assert.NotNull(result.Data);
            Assert.NotNull(result.Data.ClientVersion);
            Assert.Equal("Podman", result.Data.PlatformName);
        }

        [Fact]
        public async Task IsLinuxEngine_ReturnsTrue()
        {
            var result = await SystemDriver.IsLinuxEngineAsync(Context);
            Assert.True(result.Success);
            Assert.True(result.Data);
        }

        [Fact]
        public async Task IsWindowsEngine_ReturnsFalse()
        {
            var result = await SystemDriver.IsWindowsEngineAsync(Context);
            Assert.True(result.Success);
            Assert.False(result.Data);
        }

        [Fact]
        public async Task SwitchToWindowsDaemon_Fails()
        {
            var result = await SystemDriver.SwitchToWindowsDaemonAsync(Context);
            Assert.False(result.Success);
        }
    }
}
