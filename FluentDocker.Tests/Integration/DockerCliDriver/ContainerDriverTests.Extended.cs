using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Extended container driver tests: WaitAsync, UpdateAsync, DiffAsync, ExportAsync.
  /// </summary>
  public partial class ContainerDriverTests
  {
    #region WaitAsync Tests

    [Fact]
    public async Task Wait_ShortLivedContainer_ReturnsZeroExitCode()
    {
      string? containerId = null;
      try
      {
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "true" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        var waitResult = await ContainerDriver.WaitAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(waitResult.Success, $"Wait failed: {waitResult.Error}");
        Assert.Equal(0, waitResult.Data.ExitCode);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Wait_ContainerExitsWithError_ReturnsNonZeroExitCode()
    {
      string? containerId = null;
      try
      {
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sh", "-c", "exit 42" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        var waitResult = await ContainerDriver.WaitAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(waitResult.Success, $"Wait failed: {waitResult.Error}");
        Assert.Equal(42, waitResult.Data.ExitCode);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Wait_AlreadyExitedContainer_ReturnsImmediately()
    {
      string? containerId = null;
      try
      {
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "echo", "done" },
          Detach = true
        }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        // Container should exit almost immediately
        await Task.Delay(2000, TestContext.Current.CancellationToken);

        // WaitAsync on already-exited container should return immediately
        var waitResult = await ContainerDriver.WaitAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(waitResult.Success, $"Wait failed: {waitResult.Error}");
        Assert.Equal(0, waitResult.Data.ExitCode);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Wait_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.WaitAsync(Context, fakeId, TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task Update_MemoryLimit_Succeeds()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        var updateResult = await ContainerDriver.UpdateAsync(Context, containerId,
            new ContainerUpdateConfig
            {
              MemoryLimit = 128 * 1024 * 1024,
              MemorySwap = 256 * 1024 * 1024
            }, TestContext.Current.CancellationToken);

        Assert.True(updateResult.Success, $"Update failed: {updateResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Update_CpuShares_Succeeds()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        var updateResult = await ContainerDriver.UpdateAsync(Context, containerId,
            new ContainerUpdateConfig { CpuShares = 512 }, TestContext.Current.CancellationToken);

        Assert.True(updateResult.Success, $"Update failed: {updateResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Update_MultipleConstraints_Succeeds()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        var updateResult = await ContainerDriver.UpdateAsync(Context, containerId,
            new ContainerUpdateConfig
            {
              MemoryLimit = 256 * 1024 * 1024,
              MemorySwap = 512 * 1024 * 1024,
              CpuShares = 256,
              PidsLimit = 100
            }, TestContext.Current.CancellationToken);

        Assert.True(updateResult.Success, $"Update failed: {updateResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Update_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.UpdateAsync(Context, fakeId,
          new ContainerUpdateConfig { MemoryLimit = 128 * 1024 * 1024 }, TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region DiffAsync Tests

    [Fact]
    public async Task Diff_AfterFileCreation_ShowsAddedFile()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "touch", "/tmp/newfile.txt" }
        }, TestContext.Current.CancellationToken);

        var diffResult = await ContainerDriver.DiffAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(diffResult.Success, $"Diff failed: {diffResult.Error}");
        Assert.NotNull(diffResult.Data);
        Assert.True(diffResult.Data.Count > 0, "Should have filesystem changes");
        Assert.Contains(diffResult.Data,
            d => d.Path.Contains("newfile.txt") && d.Kind == "A");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Diff_AfterFileModification_ShowsChangedFile()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "touch", "/tmp/diff-test-file" }
        }, TestContext.Current.CancellationToken);

        var diffResult = await ContainerDriver.DiffAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(diffResult.Success, $"Diff failed: {diffResult.Error}");
        Assert.NotNull(diffResult.Data);
        Assert.Contains(diffResult.Data,
            d => d.Path.Contains("diff-test-file") && d.Kind == "A");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Diff_AfterFileDeletion_ShowsDeletedFile()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "rm", "/etc/motd" }
        }, TestContext.Current.CancellationToken);

        var diffResult = await ContainerDriver.DiffAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(diffResult.Success, $"Diff failed: {diffResult.Error}");
        Assert.NotNull(diffResult.Data);
        Assert.Contains(diffResult.Data,
            d => d.Path.Contains("motd") && d.Kind == "D");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Diff_MultipleChanges_ReturnsAllTypes()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        // Add, change, and delete in one go
        await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "sh", "-c",
              "touch /tmp/added.txt && echo x >> /etc/hostname && rm /etc/motd" }
        }, TestContext.Current.CancellationToken);

        var diffResult = await ContainerDriver.DiffAsync(Context, containerId, TestContext.Current.CancellationToken);

        Assert.True(diffResult.Success, $"Diff failed: {diffResult.Error}");
        Assert.NotNull(diffResult.Data);
        Assert.True(diffResult.Data.Count >= 3,
            $"Expected at least 3 changes, got {diffResult.Data.Count}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Diff_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.DiffAsync(Context, fakeId, TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task Export_RunningContainer_CreatesArchive()
    {
      string? containerId = null;
      var outputPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.tar");
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });

        var exportResult = await ContainerDriver.ExportAsync(
            Context, containerId, outputPath, TestContext.Current.CancellationToken);

        Assert.True(exportResult.Success, $"Export failed: {exportResult.Error}");
        Assert.True(File.Exists(outputPath), "Export tar file should exist");
        Assert.True(new FileInfo(outputPath).Length > 0,
            "Export tar should not be empty");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
        if (File.Exists(outputPath))
          File.Delete(outputPath);
      }
    }

    [Fact]
    public async Task Export_StoppedContainer_CreatesArchive()
    {
      string? containerId = null;
      var outputPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.tar");
      try
      {
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });
        await ContainerDriver.StopAsync(Context, containerId, timeout: 5, TestContext.Current.CancellationToken);

        var exportResult = await ContainerDriver.ExportAsync(
            Context, containerId, outputPath, TestContext.Current.CancellationToken);

        Assert.True(exportResult.Success, $"Export failed: {exportResult.Error}");
        Assert.True(File.Exists(outputPath), "Export tar file should exist");
        Assert.True(new FileInfo(outputPath).Length > 0,
            "Export tar should not be empty");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
        if (File.Exists(outputPath))
          File.Delete(outputPath);
      }
    }

    [Fact]
    public async Task Export_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var outputPath = Path.Combine(Path.GetTempPath(), $"export-fail-{Guid.NewGuid():N}.tar");
      try
      {
        var result = await ContainerDriver.ExportAsync(Context, fakeId, outputPath, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
      }
      finally
      {
        if (File.Exists(outputPath))
          File.Delete(outputPath);
      }
    }

    #endregion
  }
}
