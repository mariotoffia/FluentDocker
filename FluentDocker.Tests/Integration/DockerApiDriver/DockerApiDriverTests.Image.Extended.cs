using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
  /// <summary>
  /// Extended Docker API integration tests for image operations:
  /// Build, Save, Load, Import, Push.
  /// </summary>
  public partial class DockerApiDriverTests
  {
    #region Image Build Tests

    [Fact]
    public async Task Image_Build_SimpleDockerfile_ReturnsImageId()
    {
      var tempDir = Path.Combine(Path.GetTempPath(), $"fd-api-build-{Guid.NewGuid():N}");
      var tag = UniqueName("fd-api-bld") + ":latest";

      try
      {
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "Dockerfile"),
            "FROM alpine:latest\nRUN echo hello\n", cancellationToken: TestContext.Current.CancellationToken);

        await EnsureImageAsync(TestImage);

        var result = await ImageDriver.BuildAsync(Context, new ImageBuildConfig
        {
          BuildContext = tempDir,
          Tags = [tag]
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"Build failed: {result.Error}");
        Assert.NotNull(result.Data);
        Assert.False(string.IsNullOrEmpty(result.Data.ImageId),
            "ImageId should not be empty");
      }
      finally
      {
        try
        { await ImageDriver.RemoveAsync(Context, tag, force: true, cancellationToken: TestContext.Current.CancellationToken); }
        catch { }

        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, recursive: true);
      }
    }

    #endregion

    #region Image Save Tests

    [Fact]
    public async Task Image_Save_WritesToTar()
    {
      var tarPath = Path.Combine(Path.GetTempPath(), $"fd-api-save-{Guid.NewGuid():N}.tar");

      try
      {
        await EnsureImageAsync(TestImage);

        var result = await ImageDriver.SaveAsync(
            Context, ["alpine:latest"], tarPath, cancellationToken: TestContext.Current.CancellationToken);

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

    #endregion

    #region Image Load Tests

    [Fact]
    public async Task Image_Load_FromSavedTar_Succeeds()
    {
      var tarPath = Path.Combine(Path.GetTempPath(), $"fd-api-load-{Guid.NewGuid():N}.tar");

      try
      {
        await EnsureImageAsync(TestImage);

        var saveResult = await ImageDriver.SaveAsync(
            Context, ["alpine:latest"], tarPath, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(saveResult.Success, $"Save failed: {saveResult.Error}");

        var loadResult = await ImageDriver.LoadAsync(Context, tarPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(loadResult.Success, $"Load failed: {loadResult.Error}");
      }
      finally
      {
        if (File.Exists(tarPath))
          File.Delete(tarPath);
      }
    }

    #endregion

    #region Image Import Tests

    [Fact]
    public async Task Image_Import_ContainerExport_CreatesImage()
    {
      string? containerId = null;
      var tarPath = Path.Combine(Path.GetTempPath(), $"fd-api-import-{Guid.NewGuid():N}.tar");
      var repo = UniqueName("fd-api-imp");
      var tag = "v1";

      try
      {
        await EnsureImageAsync(TestImage);

        // Create and run a container (no RunContainerAsync in API base)
        var createResult = await ContainerDriver.CreateAsync(Context, new ContainerCreateConfig
        {
          Image = "alpine:latest",
          Name = UniqueName("fd-api-exp"),
          Command = ["sh", "-c", "echo imported"]
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
        containerId = createResult.Data.Id;

        await ContainerDriver.StartAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(2000, cancellationToken: TestContext.Current.CancellationToken);

        // Export the container filesystem
        var exportResult = await ContainerDriver.ExportAsync(
            Context, containerId, tarPath, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(exportResult.Success, $"Export failed: {exportResult.Error}");

        // Import as a new image
        var importResult = await ImageDriver.ImportAsync(
            Context, tarPath, repo, tag, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(importResult.Success, $"Import failed: {importResult.Error}");
        Assert.False(string.IsNullOrEmpty(importResult.Data),
            "Imported image ID should not be empty");
      }
      finally
      {
        if (containerId != null)
        {
          try
          { await ContainerDriver.StopAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken); }
          catch { }
          try
          { await ContainerDriver.RemoveAsync(Context, containerId, force: true, cancellationToken: TestContext.Current.CancellationToken); }
          catch { }
        }

        try
        { await ImageDriver.RemoveAsync(Context, $"{repo}:{tag}", force: true, cancellationToken: TestContext.Current.CancellationToken); }
        catch { }

        if (File.Exists(tarPath))
          File.Delete(tarPath);
      }
    }

    #endregion

    #region Image Push Tests

    [Fact]
    [Trait("Category", "ManualOnly")]
    public async Task Image_Push_ToNonExistentRegistry_Fails()
    {
      var testRepo = "localhost:9999/fd-api-push-test";
      var testTag = "latest";

      try
      {
        await EnsureImageAsync(TestImage);
        await ImageDriver.TagAsync(Context, "alpine:latest", testRepo, testTag, cancellationToken: TestContext.Current.CancellationToken);

        var result = await ImageDriver.PushAsync(
            Context, $"{testRepo}:{testTag}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success, "Push to non-existent registry should fail");
      }
      finally
      {
        try
        { await ImageDriver.RemoveAsync(Context, $"{testRepo}:{testTag}", cancellationToken: TestContext.Current.CancellationToken); }
        catch { }
      }
    }

    #endregion
  }
}
