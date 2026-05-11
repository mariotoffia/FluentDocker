using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IStreamDriver: StreamLogsAsync, StreamEventsAsync,
  /// StreamStatsAsync.
  /// </summary>
  [Trait("Category", "Integration")]
  [Collection("DockerDriver")]
  public class StreamDriverTests : DockerDriverTestBase
  {
    protected IStreamDriver StreamDriver =>
        Kernel.SysCtl<IStreamDriver>(DriverId);

    #region StreamLogsAsync Tests

    [Fact]
    public async Task StreamLogs_RunningContainer_ReturnsLogEntries()
    {
      string? containerId = null;
      try
      {
        var runResult = await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = [ "sh", "-c",
                  "for i in 1 2 3 4 5; do echo \"log-line-$i\"; done" ],
              Detach = true
            }, TestContext.Current.CancellationToken);
        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;

        // Wait for output
        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var entries = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await foreach (var line in StreamDriver.StreamLogsAsync(
            Context, containerId,
            new StreamLogsConfig { Follow = false, Stdout = true },
            cts.Token))
        {
          entries.Add(line);
          if (entries.Count >= 5)
            break;
        }

        Assert.True(entries.Count >= 1,
            $"Expected at least 1 log entry, got {entries.Count}");
        Assert.Contains(entries, l => l.Contains("log-line-"));
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task StreamLogs_WithTail_LimitsOutput()
    {
      string? containerId = null;
      try
      {
        var runResult = await ContainerDriver.RunAsync(Context,
            new ContainerCreateConfig
            {
              Image = TestImage,
              Command = [ "sh", "-c",
                  "for i in $(seq 1 20); do echo \"line-$i\"; done" ],
              Detach = true
            }, TestContext.Current.CancellationToken);
        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;
        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var entries = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await foreach (var line in StreamDriver.StreamLogsAsync(
            Context, containerId,
            new StreamLogsConfig { Follow = false, Tail = 5 },
            cts.Token))
        {
          entries.Add(line);
        }

        Assert.True(entries.Count <= 6,
            $"Expected at most ~5 entries with tail=5, got {entries.Count}");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region StreamEventsAsync Tests

    [Fact]
    public async Task StreamEvents_ContainerCreate_CapturesEvent()
    {
      string? containerId = null;
      try
      {
        var events = new List<ContainerEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Start listening for events in a background task
        var listenTask = Task.Run(async () =>
        {
          await foreach (var evt in StreamDriver.StreamEventsAsync(
              Context,
              new StreamEventsConfig
              {
                Types = ["container"],
                Actions = ["create", "start"]
              },
              cts.Token))
          {
            events.Add(evt);
            if (events.Count >= 2)
              break;
          }
        }, cts.Token);

        // Give the listener a moment to start
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        // Trigger a container create + start event
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig { Command = ["sleep", "30"] });

        // Wait for events to arrive
        try
        { await listenTask; }
        catch (OperationCanceledException) { }

        Assert.True(events.Count >= 1,
            $"Expected at least 1 event, got {events.Count}");
        Assert.Contains(events,
            e => e.Type == "container" &&
                 (e.Action == "create" || e.Action == "start"));
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region StreamStatsAsync Tests

    [Fact]
    public async Task StreamStats_RunningContainer_ReturnsCpuMemory()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(NginxImage);

        // Allow container to stabilize before collecting stats
        await Task.Delay(3000, TestContext.Current.CancellationToken);

        var stats = new List<ContainerStats>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await foreach (var stat in StreamDriver.StreamStatsAsync(
            Context, containerId,
            new StreamStatsConfig { Stream = true },
            cts.Token))
        {
          stats.Add(stat);
          if (stats.Count >= 2)
            break;
        }

        Assert.True(stats.Count >= 1,
            $"Expected at least 1 stats entry, got {stats.Count}");
        var first = stats[0];
        Assert.True(first.MemoryLimit > 0,
            "Memory limit should be positive");
        Assert.True(first.CpuPercentage >= 0,
            "CPU percentage should be >= 0");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region AttachAsync Tests

    [Fact]
    public async Task AttachAsync_ToRunningContainer_ReturnsConnectedResult()
    {
      // NOTE: Container must NOT use Tty=true because the CLI attach process
      // uses redirected stdio (not a real TTY), and docker attach to a TTY
      // container fails with "the input device is not a TTY".
      string? containerId = null;
      AttachResult attachResult = null;
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = [ "sh", "-c",
                  "while true; do echo hello; sleep 1; done" ],
              Interactive = true
            });

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var result = await StreamDriver.AttachAsync(Context, containerId,
            new AttachConfig { Stdout = true }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"AttachAsync failed: {result.Error}");
        attachResult = result.Data;
        Assert.True(attachResult.IsConnected, "Expected IsConnected to be true");
        Assert.NotNull(attachResult.OutputStream);
      }
      finally
      {
        if (attachResult != null)
          await attachResult.DisposeAsync();
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task AttachAsync_ToRunningContainer_CanReadOutput()
    {
      string? containerId = null;
      AttachResult attachResult = null;
      try
      {
        // Continuous output so we always catch new lines after attaching
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = [ "sh", "-c",
                  "while true; do echo attach-output-line; sleep 1; done" ],
              Interactive = true
            });

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var result = await StreamDriver.AttachAsync(Context, containerId,
            new AttachConfig { Stdout = true }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"AttachAsync failed: {result.Error}");
        attachResult = result.Data;

        // Read output with CancellationToken-based timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        try
        {
          while (!cts.Token.IsCancellationRequested)
          {
            var bytesRead = await attachResult.OutputStream.ReadAsync(
                buffer.AsMemory(0, buffer.Length), cts.Token);
            if (bytesRead == 0)
              break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            if (sb.ToString().Contains("attach-output-"))
              break;
          }
        }
        catch (OperationCanceledException) { }

        var output = sb.ToString();
        Assert.True(output.Contains("attach-output-"),
            $"Expected output to contain 'attach-output-', got: '{output}'");
      }
      finally
      {
        if (attachResult != null)
          await attachResult.DisposeAsync();
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task AttachAsync_DisposeAsync_DisconnectsCleanly()
    {
      string? containerId = null;
      try
      {
        containerId = await RunContainerAsync(TestImage,
            new ContainerCreateConfig
            {
              Command = ["sleep", "300"],
              Interactive = true
            });

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var result = await StreamDriver.AttachAsync(Context, containerId,
            new AttachConfig { Stdout = true }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"AttachAsync failed: {result.Error}");

        var attachResult = result.Data;
        Assert.True(attachResult.IsConnected);

        // Dispose should disconnect cleanly
        await attachResult.DisposeAsync();
        Assert.False(attachResult.IsConnected,
            "Expected IsConnected to be false after dispose");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    #endregion
  }
}
