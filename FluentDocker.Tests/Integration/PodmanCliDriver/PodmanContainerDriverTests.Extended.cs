using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Extended Podman container driver tests: WaitAsync, TopAsync, DiffAsync,
  /// CopyToAsync, CopyFromAsync.
  /// </summary>
  public partial class PodmanContainerDriverTests
  {
    #region WaitAsync Tests

    [Fact]
    public async Task Wait_ShortLivedContainer_ReturnsZeroExitCode()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        var result = await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "true" },
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        var waitResult = await ContainerDriver.WaitAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(waitResult.Success,
            $"Wait failed: {waitResult.Error}");
        Assert.Equal(0, waitResult.Data.ExitCode);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Wait_ContainerExitsWithError_ReturnsNonZeroExitCode()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        // Use a short sleep before exit to give the Podman machine VM
        // time to fully start the container and register it.
        var result = await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = new[] { "sh", "-c", "sleep 1; exit 17" },
              Detach = true
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        var waitResult = await ContainerDriver.WaitAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(waitResult.Success,
            $"Wait failed: {waitResult.Error}");
        Assert.Equal(17, waitResult.Data.ExitCode);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Wait_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.WaitAsync(Context, fakeId, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region TopAsync Tests

    [Fact]
    public async Task Top_RunningContainer_ReturnsProcesses()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "60" }
            });

        var topResult = await ContainerDriver.TopAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(topResult.Success,
            $"Top failed: {topResult.Error}");
        Assert.NotNull(topResult.Data);
        Assert.True(topResult.Data.Processes.Count > 0,
            "Should have at least one process");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region DiffAsync Tests

    [Fact]
    public async Task Diff_AfterFileCreation_ShowsAddedFile()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "60" }
            });

        await ContainerDriver.ExecAsync(Context, containerId,
            new ExecConfig
            {
              Command = new[] { "touch", "/tmp/podman-diff-test.txt" }
            }, cancellationToken: TestContext.Current.CancellationToken);

        var diffResult = await ContainerDriver.DiffAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(diffResult.Success,
            $"Diff failed: {diffResult.Error}");
        Assert.NotNull(diffResult.Data);
        Assert.Contains(diffResult.Data,
            d => d.Path.Contains("podman-diff-test") && d.Kind == "A");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Diff_NonExistentContainer_Fails()
    {
      var fakeId = "nonexistent" + Guid.NewGuid().ToString("N")[..12];
      var result = await ContainerDriver.DiffAsync(Context, fakeId, cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
    }

    #endregion

    #region CopyToAsync Tests

    [Fact]
    public async Task CopyTo_FileToContainer_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      var tempFile = Path.GetTempFileName();
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "60" }
            });
        File.WriteAllText(tempFile, "podman-copy-test");

        var copyResult = await ContainerDriver.CopyToAsync(
            Context, containerId, tempFile, "/tmp/copied.txt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(copyResult.Success,
            $"CopyTo failed: {copyResult.Error}");

        // Verify
        var execResult = await ContainerDriver.ExecAsync(
            Context, containerId,
            new ExecConfig
            {
              Command = new[] { "cat", "/tmp/copied.txt" }
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(execResult.Success);
        Assert.Contains("podman-copy-test", execResult.Data.StdOut);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        if (File.Exists(tempFile))
          File.Delete(tempFile);
      }
    }

    #endregion

    #region CopyFromAsync Tests

    [Fact]
    public async Task CopyFrom_FileFromContainer_Succeeds()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      var tempDir = Path.Combine(
          Path.GetTempPath(), Guid.NewGuid().ToString());
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sleep", "60" }
            });
        Directory.CreateDirectory(tempDir);

        await ContainerDriver.ExecAsync(Context, containerId,
            new ExecConfig
            {
              Command = new[] { "sh", "-c",
                  "echo 'podman-from-container' > /tmp/from-container.txt" }
            }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(200, cancellationToken: TestContext.Current.CancellationToken);

        var copyResult = await ContainerDriver.CopyFromAsync(
            Context, containerId, "/tmp/from-container.txt", tempDir, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(copyResult.Success,
            $"CopyFrom failed: {copyResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        if (Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    #endregion

    #region StatsAsync Tests

    [Fact]
    public async Task Stats_RunningContainer_ReturnsResourceUsage()
    {
      await EnsureImageAsync(NginxImage);
      string containerId = null;
      try
      {
        containerId = await RunContainerAsync(NginxImage);

        var statsResult = await ContainerDriver.StatsAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(statsResult.Success,
            $"Stats failed: {statsResult.Error}");
        Assert.NotNull(statsResult.Data);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region KillAsync Tests

    [Fact]
    public async Task Kill_RunningContainer_TerminatesContainer()
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

        var killResult = await ContainerDriver.KillAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(killResult.Success,
            $"Kill failed: {killResult.Error}");

        // Verify container is no longer running
        await Task.Delay(500, cancellationToken: TestContext.Current.CancellationToken);
        var inspectResult = await ContainerDriver.InspectAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        if (inspectResult.Success)
        {
          Assert.NotEqual("running",
              inspectResult.Data.State?.Status?.ToLower());
        }
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Kill_WithSignal_SendsSpecifiedSignal()
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

        var killResult = await ContainerDriver.KillAsync(
            Context, containerId, signal: "SIGTERM", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(killResult.Success,
            $"Kill SIGTERM failed: {killResult.Error}");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region PauseAsync / UnpauseAsync Tests

    [Fact]
    public async Task PauseUnpause_RunningContainer_PausesAndResumes()
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

        // Pause
        var pauseResult = await ContainerDriver.PauseAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(pauseResult.Success,
            $"Pause failed: {pauseResult.Error}");

        var inspectResult = await ContainerDriver.InspectAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.Equal("paused",
            inspectResult.Data.State?.Status?.ToLower());

        // Unpause
        var unpauseResult = await ContainerDriver.UnpauseAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(unpauseResult.Success,
            $"Unpause failed: {unpauseResult.Error}");

        inspectResult = await ContainerDriver.InspectAsync(
            Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.Equal("running",
            inspectResult.Data.State?.Status?.ToLower());
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region GetLogsAsync Tests

    [Fact]
    public async Task GetLogs_ContainerWithOutput_ReturnsLogText()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        // Use a running container that echoes then sleeps to avoid
        // race between container exit and log retrieval
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sh", "-c",
                  "echo podman-log-line-1; echo podman-log-line-2; sleep 300" }
            });

        // Podman machine (VM) may need extra time for container to start
        // and flush log output to the log driver.
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);

        // Retry with content check — ReadToEnd may return "\n" before
        // actual log lines are available.
        string? logs = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
          var logsResult = await ContainerDriver.GetLogsAsync(
              Context, containerId, tail: 10, cancellationToken: TestContext.Current.CancellationToken);
          Assert.True(logsResult.Success,
              $"GetLogs failed: {logsResult.Error}");
          logs = logsResult.Data?.Trim();
          if (!string.IsNullOrWhiteSpace(logs))
            break;
          await Task.Delay(2000, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.True(!string.IsNullOrWhiteSpace(logs),
            "Expected non-empty logs output");
        Assert.Contains("podman-log-line-1", logs);
        Assert.Contains("podman-log-line-2", logs);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task GetLogs_WithTail_LimitsOutput()
    {
      await EnsureImageAsync(TestImage);
      string containerId = null;
      try
      {
        // Use a running container to avoid timing issues
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = new[] { "sh", "-c",
                  "for i in $(seq 1 20); do echo line-$i; done; sleep 300" }
            });

        // Podman machine (VM) may need extra time for output to flush
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);

        // Retry until actual content is available
        var lines = Array.Empty<string>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
          var logsResult = await ContainerDriver.GetLogsAsync(
              Context, containerId, tail: 3, cancellationToken: TestContext.Current.CancellationToken);
          Assert.True(logsResult.Success,
              $"GetLogs failed: {logsResult.Error}");
          lines = logsResult.Data.Split('\n',
              StringSplitOptions.RemoveEmptyEntries);
          if (lines.Length > 0)
            break;
          await Task.Delay(2000, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.True(lines.Length > 0, "Expected some log output");
        Assert.True(lines.Length <= 5,
            $"Expected at most ~3 lines with tail=3, got {lines.Length}");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    #endregion
  }
}
