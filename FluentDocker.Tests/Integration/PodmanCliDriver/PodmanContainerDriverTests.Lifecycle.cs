using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Extended Podman container lifecycle tests: UpdateAsync, ExportAsync,
  /// RenameAsync.
  /// </summary>
  public partial class PodmanContainerDriverTests
  {
    #region UpdateAsync Tests

    [Fact]
    public async Task Update_MemoryLimit_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "300" }
            });

        var updateResult = await ContainerDriver.UpdateAsync(
            Context, containerId,
            new ContainerUpdateConfig
            {
              MemoryLimit = 128 * 1024 * 1024 // 128 MB
            });

        Assert.True(updateResult.Success,
            $"Update failed: {updateResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Update_RestartPolicy_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "300" }
            });

        var updateResult = await ContainerDriver.UpdateAsync(
            Context, containerId,
            new ContainerUpdateConfig
            {
              RestartPolicy = "on-failure"
            });

        Assert.True(updateResult.Success,
            $"Update restart policy failed: {updateResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task Export_RunningContainer_CreatesArchive()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      var exportPath = Path.Combine(Path.GetTempPath(),
          $"podman-export-{Guid.NewGuid():N}.tar");
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "60" }
            });

        var exportResult = await ContainerDriver.ExportAsync(
            Context, containerId, exportPath);

        Assert.True(exportResult.Success,
            $"Export failed: {exportResult.Error}");
        Assert.True(File.Exists(exportPath),
            "Export archive should exist");
        Assert.True(new FileInfo(exportPath).Length > 0,
            "Export archive should not be empty");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        if (File.Exists(exportPath)) File.Delete(exportPath);
      }
    }

    [Fact]
    public async Task Export_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var exportPath = Path.Combine(Path.GetTempPath(),
          $"podman-export-fail-{Guid.NewGuid():N}.tar");
      try
      {
        var result = await ContainerDriver.ExportAsync(
            Context, fakeId, exportPath);
        Assert.False(result.Success);
      }
      finally
      {
        if (File.Exists(exportPath)) File.Delete(exportPath);
      }
    }

    #endregion

    #region RenameAsync Tests

    [Fact]
    public async Task Rename_RunningContainer_ChangesName()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        var originalName = UniqueName("rename-orig");
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Name = originalName,
              Command = new[] { "sleep", "300" }
            });

        var newName = UniqueName("rename-new");
        var renameResult = await ContainerDriver.RenameAsync(
            Context, containerId, newName);

        Assert.True(renameResult.Success,
            $"Rename failed: {renameResult.Error}");

        // Verify name changed via inspect
        var inspectResult = await ContainerDriver.InspectAsync(
            Context, containerId);
        Assert.True(inspectResult.Success);
        Assert.Contains(newName, inspectResult.Data.Name);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion
  }
}
