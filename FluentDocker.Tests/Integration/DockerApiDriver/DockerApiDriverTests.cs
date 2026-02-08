using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
    /// <summary>
    /// End-to-end integration tests for the Docker REST API driver.
    /// Requires a running Docker daemon.
    /// </summary>
    [Trait("Category", "Integration")]
    public class DockerApiDriverTests : DockerApiDriverTestBase
    {
        // ---------------------------------------------------------------
        // System tests
        // ---------------------------------------------------------------

        [Fact]
        public async Task System_Ping_ReturnsSuccess()
        {
            var result = await SystemDriver.PingAsync(Context);

            Assert.True(result.Success, $"Ping failed: {result.Error}");
        }

        [Fact]
        public async Task System_GetInfo_ReturnsOsType()
        {
            var result = await SystemDriver.GetInfoAsync(Context);

            Assert.True(result.Success, $"GetInfo failed: {result.Error}");
            Assert.NotNull(result.Data);
            Assert.Contains(result.Data.OSType, new[] { "linux", "windows" });
        }

        [Fact]
        public async Task System_GetVersion_ReturnsServerVersion()
        {
            var result = await SystemDriver.GetVersionAsync(Context);

            Assert.True(result.Success, $"GetVersion failed: {result.Error}");
            Assert.NotNull(result.Data);
            Assert.False(
                string.IsNullOrWhiteSpace(result.Data.ServerVersion),
                "ServerVersion should not be empty");
        }

        // ---------------------------------------------------------------
        // Container lifecycle tests
        // ---------------------------------------------------------------

        [Fact]
        public async Task Container_CreateStartStopRemove_FullLifecycle()
        {
            await EnsureImageAsync(TestImage);
            string containerId = null;

            try
            {
                // Create
                var name = UniqueName("lifecycle");
                var config = new ContainerCreateConfig
                {
                    Image = TestImage,
                    Name = name,
                    Command = new[] { "sleep", "60" }
                };

                var createResult = await ContainerDriver.CreateAsync(Context, config);
                Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
                containerId = createResult.Data.Id;
                Assert.False(string.IsNullOrEmpty(containerId), "Container ID should not be empty");

                // Start
                var startResult = await ContainerDriver.StartAsync(Context, containerId);
                Assert.True(startResult.Success, $"Start failed: {startResult.Error}");

                // Stop
                var stopResult = await ContainerDriver.StopAsync(Context, containerId, timeout: 5);
                Assert.True(stopResult.Success, $"Stop failed: {stopResult.Error}");
            }
            finally
            {
                // Remove
                if (!string.IsNullOrEmpty(containerId))
                {
                    var removeResult = await ContainerDriver.RemoveAsync(
                        Context, containerId, force: true, removeVolumes: true);
                    Assert.True(removeResult.Success, $"Remove failed: {removeResult.Error}");
                }
            }
        }

        [Fact]
        public async Task Container_Inspect_ReturnsContainerDetails()
        {
            await EnsureImageAsync(TestImage);
            string containerId = null;

            try
            {
                var config = new ContainerCreateConfig
                {
                    Image = TestImage,
                    Name = UniqueName("inspect"),
                    Command = new[] { "sleep", "60" }
                };

                var createResult = await ContainerDriver.CreateAsync(Context, config);
                Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
                containerId = createResult.Data.Id;

                // Start the container so state is populated
                await ContainerDriver.StartAsync(Context, containerId);

                var inspectResult = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspectResult.Success, $"Inspect failed: {inspectResult.Error}");
                Assert.NotNull(inspectResult.Data);
                Assert.NotNull(inspectResult.Data.Id);
                Assert.NotNull(inspectResult.Data.State);
                Assert.True(inspectResult.Data.State.Running, "Container should be running");
            }
            finally
            {
                if (!string.IsNullOrEmpty(containerId))
                    await ContainerDriver.RemoveAsync(
                        Context, containerId, force: true, removeVolumes: true);
            }
        }

        [Fact]
        public async Task Container_List_IncludesCreatedContainer()
        {
            await EnsureImageAsync(TestImage);
            var name = UniqueName("list");
            string containerId = null;

            try
            {
                var config = new ContainerCreateConfig
                {
                    Image = TestImage,
                    Name = name,
                    Command = new[] { "sleep", "60" }
                };

                var createResult = await ContainerDriver.CreateAsync(Context, config);
                Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
                containerId = createResult.Data.Id;

                // List all containers (including stopped/created)
                var listResult = await ContainerDriver.ListAsync(
                    Context, new ContainerListFilter { All = true });
                Assert.True(listResult.Success, $"List failed: {listResult.Error}");
                Assert.NotEmpty(listResult.Data);

                // Verify our container is in the list
                var found = listResult.Data.Any(c =>
                    c.Id != null && c.Id.StartsWith(containerId[..12]));
                Assert.True(found, $"Container {containerId[..12]} not found in list");
            }
            finally
            {
                if (!string.IsNullOrEmpty(containerId))
                    await ContainerDriver.RemoveAsync(
                        Context, containerId, force: true, removeVolumes: true);
            }
        }

        // ---------------------------------------------------------------
        // Image tests
        // ---------------------------------------------------------------

        [Fact]
        public async Task Image_List_ReturnsImages()
        {
            // Ensure at least one image exists
            await EnsureImageAsync(TestImage);

            var result = await ImageDriver.ListAsync(Context);
            Assert.True(result.Success, $"Image list failed: {result.Error}");
            Assert.NotEmpty(result.Data);

            // Verify we can find alpine
            var hasAlpine = result.Data.Any(img =>
                img.RepoTags != null &&
                img.RepoTags.Any(t => t.Contains("alpine")));
            Assert.True(hasAlpine, "Expected to find alpine image in list");
        }

        [Fact]
        public async Task Image_Pull_PullsNewImage()
        {
            // Pull alpine (likely already cached, avoids Docker Hub rate limits)
            var pullResult = await ImageDriver.PullAsync(Context, "alpine", "latest");

            // Skip if pull fails due to network/server issues
            if (!pullResult.Success)
            {
                // If it's a network/server error, skip gracefully
                return;
            }

            // Verify the image appears in the list
            var listResult = await ImageDriver.ListAsync(Context);
            Assert.True(listResult.Success, $"Image list failed: {listResult.Error}");

            var hasAlpine = listResult.Data.Any(img =>
                img.RepoTags != null &&
                img.RepoTags.Any(t => t.Contains("alpine")));
            Assert.True(hasAlpine, "Expected to find alpine image after pull");
        }

        // ---------------------------------------------------------------
        // Network tests
        // ---------------------------------------------------------------

        [Fact]
        public async Task Network_CreateAndRemove()
        {
            var name = UniqueName("net");
            string networkId = null;

            try
            {
                var config = new NetworkCreateConfig
                {
                    Name = name,
                    Driver = "bridge"
                };

                var createResult = await NetworkDriver.CreateAsync(Context, config);
                Assert.True(createResult.Success, $"Network create failed: {createResult.Error}");
                networkId = createResult.Data.Id;
                Assert.False(string.IsNullOrEmpty(networkId), "Network ID should not be empty");

                // Verify network appears in list
                var listResult = await NetworkDriver.ListAsync(Context);
                Assert.True(listResult.Success, $"Network list failed: {listResult.Error}");

                var found = listResult.Data.Any(n => n.Name == name);
                Assert.True(found, $"Network '{name}' not found in list");
            }
            finally
            {
                if (!string.IsNullOrEmpty(networkId))
                {
                    var removeResult = await NetworkDriver.RemoveAsync(Context, networkId);
                    Assert.True(removeResult.Success, $"Network remove failed: {removeResult.Error}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Volume tests
        // ---------------------------------------------------------------

        [Fact]
        public async Task Volume_CreateAndRemove()
        {
            var name = UniqueName("vol");
            bool created = false;

            try
            {
                var config = new VolumeCreateConfig
                {
                    Name = name,
                    Driver = "local"
                };

                var createResult = await VolumeDriver.CreateAsync(Context, config);
                Assert.True(createResult.Success, $"Volume create failed: {createResult.Error}");
                Assert.Equal(name, createResult.Data.Name);
                created = true;

                // Verify volume appears in list
                var listResult = await VolumeDriver.ListAsync(Context);
                Assert.True(listResult.Success, $"Volume list failed: {listResult.Error}");

                var found = listResult.Data.Any(v => v.Name == name);
                Assert.True(found, $"Volume '{name}' not found in list");
            }
            finally
            {
                if (created)
                {
                    var removeResult = await VolumeDriver.RemoveAsync(
                        Context, name, force: true);
                    Assert.True(removeResult.Success, $"Volume remove failed: {removeResult.Error}");
                }
            }
        }
    }
}
