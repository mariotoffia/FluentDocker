using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
  /// <summary>
  /// Docker API driver integration tests for container operations:
  /// Exec, Logs, Top, Diff, Copy, Wait, Rename, Update, Export, Stats.
  /// </summary>
  public partial class DockerApiDriverTests
  {
    #region Helper Methods

    /// <summary>Label applied to all test-created containers for easy cleanup.</summary>
    private const string TestLabelKey = "com.fluentdocker.test";
    private const string TestLabelValue = "integration";

    private async Task<string> ApiRunContainerAsync(
        string image, string[] command = null)
    {
      await EnsureImageAsync(image);
      var config = new ContainerCreateConfig
      {
        Image = image,
        Command = command ?? new[] { "sleep", "60" },
        Detach = true,
        Labels = new Dictionary<string, string>
        {
          [TestLabelKey] = TestLabelValue
        }
      };
      var result = await ContainerDriver.RunAsync(Context, config);
      Assert.True(result.Success, $"Run failed: {result.Error}");
      return result.Data.Id;
    }

    private async Task ApiRemoveContainerAsync(string containerId)
    {
      if (!string.IsNullOrEmpty(containerId))
        await ContainerDriver.RemoveAsync(
            Context, containerId, force: true, removeVolumes: true);
    }

    #endregion

    #region Container Exec Tests

    [Fact]
    public async Task Container_Exec_ReturnsOutput()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var execResult = await ContainerDriver.ExecAsync(Context, containerId,
            new ExecConfig
            {
              Command = new[] { "echo", "hello-api" }
            });

        Assert.True(execResult.Success, $"Exec failed: {execResult.Error}");
        Assert.Contains("hello-api", execResult.Data.StdOut);
        Assert.Equal(0, execResult.Data.ExitCode);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Container_Exec_NonZeroExitCode()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var execResult = await ContainerDriver.ExecAsync(Context, containerId,
            new ExecConfig
            {
              Command = new[] { "sh", "-c", "exit 7" }
            });

        Assert.True(execResult.Success, $"Exec call failed: {execResult.Error}");
        Assert.Equal(7, execResult.Data.ExitCode);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Logs Tests

    [Fact]
    public async Task Container_GetLogs_ReturnsOutput()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage,
            new[] { "sh", "-c", "echo 'api-log-test'; sleep 5" });
        await Task.Delay(2000);

        var logsResult = await ContainerDriver.GetLogsAsync(
            Context, containerId, tail: 10);

        Assert.True(logsResult.Success, $"Logs failed: {logsResult.Error}");
        Assert.NotNull(logsResult.Data);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Top Tests

    [Fact]
    public async Task Container_Top_ReturnsProcesses()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var topResult = await ContainerDriver.TopAsync(Context, containerId);

        Assert.True(topResult.Success, $"Top failed: {topResult.Error}");
        Assert.NotNull(topResult.Data);
        Assert.True(topResult.Data.Processes.Count > 0,
            "Should have at least one process");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Diff Tests

    [Fact]
    public async Task Container_Diff_ShowsAddedFile()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        await ContainerDriver.ExecAsync(Context, containerId,
            new ExecConfig
            {
              Command = new[] { "touch", "/tmp/api-diff-test.txt" }
            });

        var diffResult = await ContainerDriver.DiffAsync(Context, containerId);

        Assert.True(diffResult.Success, $"Diff failed: {diffResult.Error}");
        Assert.NotNull(diffResult.Data);
        Assert.Contains(diffResult.Data,
            d => d.Path.Contains("api-diff-test") && d.Kind == "A");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Wait Tests

    [Fact]
    public async Task Container_Wait_ReturnsExitCode()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage,
            new[] { "sh", "-c", "exit 3" });

        var waitResult = await ContainerDriver.WaitAsync(Context, containerId);

        Assert.True(waitResult.Success, $"Wait failed: {waitResult.Error}");
        Assert.Equal(3, waitResult.Data.ExitCode);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Rename Tests

    [Fact]
    public async Task Container_Rename_ChangesName()
    {
      string containerId = null;
      var newName = UniqueName("api-renamed");
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var renameResult = await ContainerDriver.RenameAsync(
            Context, containerId, newName);

        Assert.True(renameResult.Success,
            $"Rename failed: {renameResult.Error}");

        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.Contains(newName, inspect.Data.Name);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Stats Tests

    [Fact]
    public async Task Container_Stats_ReturnsResourceUsage()
    {
      string containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var statsResult = await ContainerDriver.StatsAsync(
            Context, containerId);

        Assert.True(statsResult.Success,
            $"Stats failed: {statsResult.Error}");
        Assert.NotNull(statsResult.Data);
        Assert.True(statsResult.Data.MemoryLimit >= 0);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Export Tests

    [Fact]
    public async Task Container_Export_CreatesArchive()
    {
      string containerId = null;
      var outputPath = Path.Combine(
          Path.GetTempPath(), $"api-export-{Guid.NewGuid():N}.tar");
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        var exportResult = await ContainerDriver.ExportAsync(
            Context, containerId, outputPath);

        Assert.True(exportResult.Success,
            $"Export failed: {exportResult.Error}");
        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 0);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
        if (File.Exists(outputPath))
          File.Delete(outputPath);
      }
    }

    #endregion

    #region Container CopyTo/CopyFrom Tests

    [Fact]
    public async Task Container_CopyTo_CopiesFile()
    {
      string containerId = null;
      var tempFile = Path.GetTempFileName();
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);
        File.WriteAllText(tempFile, "api-copy-test-content");

        var copyResult = await ContainerDriver.CopyToAsync(
            Context, containerId, tempFile, "/tmp/api-test.txt");

        Assert.True(copyResult.Success,
            $"CopyTo failed: {copyResult.Error}");

        // Verify via exec
        var verifyResult = await ContainerDriver.ExecAsync(
            Context, containerId, new ExecConfig
            {
              Command = new[] { "cat", "/tmp/api-test.txt" }
            });
        Assert.True(verifyResult.Success);
        Assert.Contains("api-copy-test-content", verifyResult.Data.StdOut);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
        if (File.Exists(tempFile))
          File.Delete(tempFile);
      }
    }

    [Fact]
    public async Task Container_CopyFrom_CopiesFile()
    {
      string containerId = null;
      var tempDir = Path.Combine(
          Path.GetTempPath(), Guid.NewGuid().ToString());
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);
        Directory.CreateDirectory(tempDir);

        await ContainerDriver.ExecAsync(Context, containerId,
            new ExecConfig
            {
              Command = new[] { "sh", "-c",
                  "echo 'api-from-container' > /tmp/container-file.txt" }
            });
        await Task.Delay(200);

        var copyResult = await ContainerDriver.CopyFromAsync(
            Context, containerId, "/tmp/container-file.txt", tempDir);

        Assert.True(copyResult.Success,
            $"CopyFrom failed: {copyResult.Error}");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId);
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task Container_Exec_NonExistent_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.ExecAsync(Context, fakeId,
          new ExecConfig { Command = new[] { "echo" } });
      Assert.False(result.Success);
    }

    [Fact]
    public async Task Container_Wait_NonExistent_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.WaitAsync(Context, fakeId);
      Assert.False(result.Success);
    }

    #endregion
  }
}
