using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
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
          try
          { await ManifestDriver.RemoveAsync(Context, listName); }
          catch { }
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
          try
          { await ManifestDriver.RemoveAsync(Context, listName); }
          catch { }
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
          try
          { await ManifestDriver.RemoveAsync(Context, listName); }
          catch { }
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

        // Re-inspect to verify the architecture was actually set
        var verifyResult = await ManifestDriver.InspectAsync(Context, listName);
        Assert.True(verifyResult.Success, $"Re-inspect failed: {verifyResult.Error}");
        Assert.NotEmpty(verifyResult.Data.Manifests);
        var entry = verifyResult.Data.Manifests[0];
        Assert.NotNull(entry.Platform);
        Assert.Equal("arm64", entry.Platform.Architecture);
      }
      finally
      {
        if (listName != null)
          try
          { await ManifestDriver.RemoveAsync(Context, listName); }
          catch { }
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
          try
          { await ManifestDriver.RemoveAsync(Context, listName); }
          catch { }
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

    #region Push (ManualOnly — requires local registry)

    /// <summary>
    /// Creates a manifest list, adds an image, and pushes to a local registry.
    /// Requires Podman to be running and port 5053 to be available.
    /// </summary>
    [Trait("Category", "ManualOnly")]
    [Fact]
    public async Task Push_ToLocalRegistry_Succeeds()
    {
      const string registryPort = "5053";
      string registryId = null;
      string listName = null;

      try
      {
        // 1. Ensure the test image is available
        await EnsureImageAsync(TestImage);

        // 2. Start a local registry container via Podman
        registryId = await RunContainerAsync("registry:2",
            new ContainerCreateConfig
            {
              PortBindings = new Dictionary<string, string>
              {
                ["5000/tcp"] = registryPort
              }
            });
        await Task.Delay(5000); // Wait for registry to be ready (Podman VM timing)

        // 3. Create a manifest list
        listName = UniqueName("manifest");
        var createResult = await ManifestDriver.CreateAsync(
            Context, new ManifestCreateConfig { Name = listName });
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");

        // 4. Add the test image to the manifest
        var addResult = await ManifestDriver.AddAsync(
            Context, new ManifestAddConfig
            {
              ListName = listName,
              Image = TestImage
            });
        Assert.True(addResult.Success, $"Add failed: {addResult.Error}");

        // 5. Push manifest to local registry (TLS disabled for local registry)
        var pushResult = await ManifestDriver.PushAsync(
            Context, new ManifestPushConfig
            {
              ListName = listName,
              Destination = $"localhost:{registryPort}/{listName}:latest",
              TlsVerify = false,
              All = true
            });
        Assert.True(pushResult.Success, $"Push failed: {pushResult.Error}");
      }
      finally
      {
        if (listName != null)
          try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }

        await RemoveContainerAsync(registryId);
      }
    }

    /// <summary>
    /// Pushes a manifest with Rm=true to verify the list is removed after push.
    /// </summary>
    [Trait("Category", "ManualOnly")]
    [Fact]
    public async Task Push_WithRm_RemovesListAfterPush()
    {
      const string registryPort = "5054";
      string registryId = null;
      string listName = null;
      var listRemoved = false;

      try
      {
        await EnsureImageAsync(TestImage);

        registryId = await RunContainerAsync("registry:2",
            new ContainerCreateConfig
            {
              PortBindings = new Dictionary<string, string>
              {
                ["5000/tcp"] = registryPort
              }
            });
        await Task.Delay(5000);

        listName = UniqueName("manifest");
        var createResult = await ManifestDriver.CreateAsync(
            Context, new ManifestCreateConfig { Name = listName });
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");

        await ManifestDriver.AddAsync(
            Context, new ManifestAddConfig
            {
              ListName = listName,
              Image = TestImage
            });

        // Push with Rm = true — manifest list should be removed after push
        var pushResult = await ManifestDriver.PushAsync(
            Context, new ManifestPushConfig
            {
              ListName = listName,
              Destination = $"localhost:{registryPort}/{listName}:latest",
              TlsVerify = false,
              All = true,
              Rm = true
            });
        Assert.True(pushResult.Success, $"Push failed: {pushResult.Error}");

        // Verify the manifest list no longer exists locally
        var existsResult = await ManifestDriver.ExistsAsync(Context, listName);
        Assert.True(existsResult.Success);
        Assert.False(existsResult.Data, "Manifest list should be removed after push with Rm=true");
        listRemoved = true;
      }
      finally
      {
        if (listName != null && !listRemoved)
          try { await ManifestDriver.RemoveAsync(Context, listName); } catch { }

        await RemoveContainerAsync(registryId);
      }
    }

    #endregion
  }
}
