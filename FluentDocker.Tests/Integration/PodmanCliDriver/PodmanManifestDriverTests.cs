using System;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
    /// <summary>
    /// Integration tests for Podman manifest (multi-arch) driver.
    /// Requires Podman to be installed.
    /// </summary>
    [Collection("PodmanDriver")]
    [Trait("Category", "PodmanIntegration")]
    public class PodmanManifestDriverTests : PodmanDriverTestBase
    {
        #region Create and Remove

        [Fact]
        public async Task Create_SimpleManifest_ReturnsId()
        {
            string listName = null;
            try
            {
                listName = UniqueName("manifest");
                var result = await ManifestDriver.CreateAsync(
                    Context, new ManifestCreateConfig { Name = listName });

                Assert.True(result.Success, $"Create failed: {result.Error}");
                Assert.False(string.IsNullOrEmpty(result.Data), "Should return manifest ID");
            }
            finally
            {
                if (listName != null)
                    try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }
            }
        }

        [Fact]
        public async Task CreateAndRemove_Succeeds()
        {
            var listName = UniqueName("manifest");
            var createResult = await ManifestDriver.CreateAsync(
                Context, new ManifestCreateConfig { Name = listName });
            Assert.True(createResult.Success, $"Create failed: {createResult.Error}");

            var removeResult = await ManifestDriver.RemoveAsync(Context, listName);
            Assert.True(removeResult.Success, $"Remove failed: {removeResult.Error}");
        }

        #endregion

        #region Add and Inspect

        [Fact]
        public async Task AddImage_ToManifest_Succeeds()
        {
            string listName = null;
            try
            {
                await EnsureImageAsync(TestImage);
                listName = UniqueName("manifest");

                await ManifestDriver.CreateAsync(
                    Context, new ManifestCreateConfig { Name = listName });

                var addResult = await ManifestDriver.AddAsync(
                    Context, new ManifestAddConfig
                    {
                        ListName = listName,
                        Image = TestImage
                    });

                Assert.True(addResult.Success, $"Add failed: {addResult.Error}");
            }
            finally
            {
                if (listName != null)
                    try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }
            }
        }

        [Fact]
        public async Task Inspect_AfterAdd_ShowsEntries()
        {
            string listName = null;
            try
            {
                await EnsureImageAsync(TestImage);
                listName = UniqueName("manifest");

                await ManifestDriver.CreateAsync(
                    Context, new ManifestCreateConfig { Name = listName });

                await ManifestDriver.AddAsync(
                    Context, new ManifestAddConfig
                    {
                        ListName = listName,
                        Image = TestImage
                    });

                var inspectResult = await ManifestDriver.InspectAsync(Context, listName);
                Assert.True(inspectResult.Success, $"Inspect failed: {inspectResult.Error}");
                Assert.NotNull(inspectResult.Data);
                Assert.NotEmpty(inspectResult.Data.Manifests);
                Assert.NotNull(inspectResult.Data.Manifests[0].Digest);
            }
            finally
            {
                if (listName != null)
                    try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }
            }
        }

        #endregion

        #region Annotate

        [Fact]
        public async Task Annotate_SetArch_Succeeds()
        {
            string listName = null;
            try
            {
                await EnsureImageAsync(TestImage);
                listName = UniqueName("manifest");

                await ManifestDriver.CreateAsync(
                    Context, new ManifestCreateConfig { Name = listName });

                await ManifestDriver.AddAsync(
                    Context, new ManifestAddConfig
                    {
                        ListName = listName,
                        Image = TestImage
                    });

                // Get the digest of the added image
                var inspectResult = await ManifestDriver.InspectAsync(Context, listName);
                Assert.True(inspectResult.Success);
                var digest = inspectResult.Data.Manifests[0].Digest;

                var annotateResult = await ManifestDriver.AnnotateAsync(
                    Context, new ManifestAnnotateConfig
                    {
                        ListName = listName,
                        Image = digest,
                        Arch = "arm64"
                    });

                Assert.True(annotateResult.Success, $"Annotate failed: {annotateResult.Error}");
            }
            finally
            {
                if (listName != null)
                    try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }
            }
        }

        #endregion

        #region Exists

        [Fact]
        public async Task Exists_AfterCreate_ReturnsTrue()
        {
            string listName = null;
            try
            {
                listName = UniqueName("manifest");
                await ManifestDriver.CreateAsync(
                    Context, new ManifestCreateConfig { Name = listName });

                var result = await ManifestDriver.ExistsAsync(Context, listName);
                Assert.True(result.Success);
                Assert.True(result.Data);
            }
            finally
            {
                if (listName != null)
                    try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }
            }
        }

        [Fact]
        public async Task Exists_NonExistent_ReturnsFalse()
        {
            var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
            var result = await ManifestDriver.ExistsAsync(Context, fakeName);
            Assert.True(result.Success);
            Assert.False(result.Data);
        }

        #endregion

        #region Error handling

        [Fact]
        public async Task Remove_NonExistent_FailsGracefully()
        {
            var fakeName = "nonexistent-" + Guid.NewGuid().ToString("N")[..12];
            var result = await ManifestDriver.RemoveAsync(Context, fakeName);
            Assert.False(result.Success);
        }

        #endregion
    }
}
