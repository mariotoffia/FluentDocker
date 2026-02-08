using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
    /// <summary>
    /// Integration tests for Podman image driver.
    /// Requires Podman to be installed.
    /// </summary>
    [Trait("Category", "PodmanIntegration")]
    public class PodmanImageDriverTests : PodmanDriverTestBase
    {
        [Fact]
        public async Task Pull_Succeeds()
        {
            var result = await ImageDriver.PullAsync(Context, "alpine", "latest");
            Assert.True(result.Success, $"Pull failed: {result.Error}");
        }

        [Fact]
        public async Task ListImages_ReturnsResults()
        {
            await EnsureImageAsync(TestImage);

            var result = await ImageDriver.ListAsync(Context);
            Assert.True(result.Success, $"List failed: {result.Error}");
            Assert.NotEmpty(result.Data);
        }

        [Fact]
        public async Task InspectImage_ReturnsDetails()
        {
            await EnsureImageAsync(TestImage);

            var result = await ImageDriver.InspectAsync(Context, TestImage);
            Assert.True(result.Success, $"Inspect failed: {result.Error}");
            Assert.NotNull(result.Data.Id);
        }

        [Fact]
        public async Task HistoryImage_ReturnsLayers()
        {
            await EnsureImageAsync(TestImage);

            var result = await ImageDriver.HistoryAsync(Context, TestImage);
            Assert.True(result.Success, $"History failed: {result.Error}");
            Assert.NotEmpty(result.Data);
        }
    }
}
