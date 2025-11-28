using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
    /// <summary>
    /// Integration tests for ISystemDriver operations.
    /// Requires Docker daemon to be running.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("DockerDriver")]
    public class SystemDriverTests : DockerDriverTestBase
    {
        [Fact]
        public async Task GetVersion_ReturnsDockerVersion()
        {
            // Act
            var result = await SystemDriver.GetVersionAsync(Context);

            // Assert
            Assert.True(result.Success, $"GetVersion failed: {result.Error}");
            Assert.NotNull(result.Data);
            Assert.NotEmpty(result.Data.ClientVersion ?? result.Data.ServerVersion);
        }

        [Fact]
        public async Task GetInfo_ReturnsSystemInfo()
        {
            // Act
            var result = await SystemDriver.GetInfoAsync(Context);

            // Assert
            Assert.True(result.Success, $"GetInfo failed: {result.Error}");
            Assert.NotNull(result.Data);
            Assert.NotEmpty(result.Data.ServerVersion);
        }

        [Fact]
        public async Task Ping_ReturnsSuccess()
        {
            // Act
            var result = await SystemDriver.PingAsync(Context);

            // Assert
            Assert.True(result.Success, $"Ping failed: {result.Error}");
        }

        [Fact]
        public async Task GetDiskUsage_ReturnsDiskInfo()
        {
            // Act
            var result = await SystemDriver.GetDiskUsageAsync(Context);

            // Assert
            Assert.True(result.Success, $"GetDiskUsage failed: {result.Error}");
            Assert.NotNull(result.Data);
        }

        [Fact]
        public async Task IsLinuxEngine_ChecksEngineType()
        {
            // Act
            var result = await SystemDriver.IsLinuxEngineAsync(Context);

            // Assert
            Assert.True(result.Success, $"IsLinuxEngine failed: {result.Error}");
            // Should be true on Linux/Mac, may be true or false on Windows
        }

        [Fact]
        public async Task GetVersion_ContainsOsInfo()
        {
            // Act
            var versionResult = await SystemDriver.GetVersionAsync(Context);

            // Assert
            Assert.True(versionResult.Success);
            Assert.NotNull(versionResult.Data.Os);
        }
    }
}

