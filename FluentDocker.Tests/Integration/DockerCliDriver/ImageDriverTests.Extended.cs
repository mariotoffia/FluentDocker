using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Extended integration tests for IImageDriver: Build, Save, Load, Import, Push.
  /// </summary>
  public partial class ImageDriverTests
  {
    [Fact]
    public async Task Build_SimpleDockerfile_ReturnsImageId()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), $"fd-build-{Guid.NewGuid():N}");
      var tag = UniqueName("fd-build") + ":latest";

      try
      {
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "Dockerfile"),
            "FROM alpine:latest\nRUN echo hello\n");

        await EnsureImageAsync(TestImage);

        var result = await ImageDriver.BuildAsync(Context, new ImageBuildConfig
        {
          BuildContext = tempDir,
          Tags = new List<string> { tag }
        });

        Assert.True(result.Success, $"Build failed: {result.Error}");
        Assert.NotNull(result.Data);
        Assert.False(string.IsNullOrEmpty(result.Data.ImageId),
            "ImageId should not be empty");
      }
      finally
      {
        try
        { await ImageDriver.RemoveAsync(Context, tag, force: true); }
        catch { }

        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, recursive: true);
      }
    }

    [Fact]
    public async Task Save_ExistingImage_WritesToTar()
    {
      var tarPath = Path.Combine(Path.GetTempPath(), $"fd-save-{Guid.NewGuid():N}.tar");

      try
      {
        await EnsureImageAsync(TestImage);

        var result = await ImageDriver.SaveAsync(
            Context, new[] { "alpine:latest" }, tarPath);

        Assert.True(result.Success, $"Save failed: {result.Error}");
        Assert.True(File.Exists(tarPath), "Tar file should exist");
        Assert.True(new FileInfo(tarPath).Length > 0, "Tar file should be non-empty");
      }
      finally
      {
        if (File.Exists(tarPath))
          File.Delete(tarPath);
      }
    }

    [Fact]
    public async Task Load_FromSavedTar_Succeeds()
    {
      var tarPath = Path.Combine(Path.GetTempPath(), $"fd-load-{Guid.NewGuid():N}.tar");

      try
      {
        await EnsureImageAsync(TestImage);

        var saveResult = await ImageDriver.SaveAsync(
            Context, new[] { "alpine:latest" }, tarPath);
        Assert.True(saveResult.Success, $"Save failed: {saveResult.Error}");

        var loadResult = await ImageDriver.LoadAsync(Context, tarPath);

        Assert.True(loadResult.Success, $"Load failed: {loadResult.Error}");
      }
      finally
      {
        if (File.Exists(tarPath))
          File.Delete(tarPath);
      }
    }

    [Fact]
    public async Task Import_ContainerExport_CreatesImage()
    {
      string containerId = null;
      var tarPath = Path.Combine(Path.GetTempPath(), $"fd-import-{Guid.NewGuid():N}.tar");
      var repo = UniqueName("fd-import");
      var tag = "v1";

      try
      {
        await EnsureImageAsync(TestImage);

        containerId = await RunContainerAsync("alpine:latest", new ContainerCreateConfig
        {
          Image = "alpine:latest",
          Command = new[] { "sh", "-c", "echo imported" },
          Detach = true
        });

        // Allow container to finish
        await Task.Delay(2000);

        var exportResult = await ContainerDriver.ExportAsync(
            Context, containerId, tarPath);
        Assert.True(exportResult.Success, $"Export failed: {exportResult.Error}");

        var importResult = await ImageDriver.ImportAsync(
            Context, tarPath, repo, tag);

        Assert.True(importResult.Success, $"Import failed: {importResult.Error}");
        Assert.False(string.IsNullOrEmpty(importResult.Data),
            "Imported image ID should not be empty");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);

        try
        { await ImageDriver.RemoveAsync(Context, $"{repo}:{tag}", force: true); }
        catch { }

        if (File.Exists(tarPath))
          File.Delete(tarPath);
      }
    }

    [Fact]
    [Trait("Category", "ManualOnly")]
    public async Task Push_ToNonExistentRegistry_Fails()
    {
      var testRepo = "localhost:9999/fd-push-test";
      var testTag = "latest";

      try
      {
        await EnsureImageAsync(TestImage);
        await ImageDriver.TagAsync(Context, "alpine:latest", testRepo, testTag);

        var result = await ImageDriver.PushAsync(
            Context, $"{testRepo}:{testTag}");

        Assert.False(result.Success, "Push to non-existent registry should fail");
      }
      finally
      {
        try
        { await ImageDriver.RemoveAsync(Context, $"{testRepo}:{testTag}"); }
        catch { }
      }
    }
  }
}
