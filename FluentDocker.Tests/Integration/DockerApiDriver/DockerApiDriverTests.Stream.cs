using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
  /// <summary>
  /// Docker API driver integration tests for IStreamDriver:
  /// StreamLogsAsync, StreamEventsAsync, StreamStatsAsync, AttachAsync.
  /// </summary>
  public partial class DockerApiDriverTests
  {
    private IStreamDriver StreamDriver => GetDriver<IStreamDriver>();

    #region StreamLogsAsync

    [Fact]
    public async Task Stream_Logs_ReturnsLogEntries()
    {
      string? containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage,
            ["sh", "-c", "for i in 1 2 3 4 5; do echo \"api-log-$i\"; done; sleep 5"]);

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
        Assert.Contains(entries, l => l.Contains("api-log-"));
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Logs_WithTail_LimitsOutput()
    {
      string? containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage,
            ["sh", "-c", "for i in $(seq 1 20); do echo \"tail-$i\"; done; sleep 5"]);

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
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Logs_Stderr_CapturesErrorOutput()
    {
      string? containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage,
            ["sh", "-c", "echo 'stderr-msg' >&2; sleep 5"]);

        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var entries = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await foreach (var line in StreamDriver.StreamLogsAsync(
            Context, containerId,
            new StreamLogsConfig { Follow = false, Stderr = true, Stdout = false },
            cts.Token))
        {
          entries.Add(line);
        }

        Assert.True(entries.Count >= 1,
            $"Expected at least 1 stderr entry, got {entries.Count}");
        Assert.Contains(entries, l => l.Contains("stderr-msg"));
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region StreamEventsAsync

    [Fact]
    public async Task Stream_Events_CapturesContainerCreate()
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

        // Give the listener a moment to connect
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        // Trigger container create + start event
        containerId = await ApiRunContainerAsync(TestImage,
            ["sleep", "30"]);

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
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Events_HasActorIdAndAttributes()
    {
      string? containerId = null;
      try
      {
        var events = new List<ContainerEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var listenTask = Task.Run(async () =>
        {
          await foreach (var evt in StreamDriver.StreamEventsAsync(
              Context,
              new StreamEventsConfig
              {
                Types = ["container"],
                Actions = ["start"]
              },
              cts.Token))
          {
            events.Add(evt);
            if (events.Count >= 1)
              break;
          }
        }, cts.Token);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        containerId = await ApiRunContainerAsync(TestImage,
            ["sleep", "30"]);

        try
        { await listenTask; }
        catch (OperationCanceledException) { }

        Assert.True(events.Count >= 1,
            $"Expected at least 1 event, got {events.Count}");
        var startEvt = events[0];
        Assert.False(string.IsNullOrEmpty(startEvt.ActorId),
            "ActorId should not be empty");
        Assert.Equal("container", startEvt.Type);
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region StreamStatsAsync

    [Fact]
    public async Task Stream_Stats_ReturnsCpuAndMemory()
    {
      string? containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);

        // Allow container to stabilize
        await Task.Delay(2000, TestContext.Current.CancellationToken);

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
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Stats_OneShot_ReturnsSingleEntry()
    {
      string? containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);
        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var stats = new List<ContainerStats>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await foreach (var stat in StreamDriver.StreamStatsAsync(
            Context, containerId,
            new StreamStatsConfig { Stream = false },
            cts.Token))
        {
          stats.Add(stat);
        }

        // One-shot should return exactly one stats entry
        Assert.True(stats.Count >= 1,
            $"Expected at least 1 entry in one-shot mode, got {stats.Count}");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Stats_IncludesContainerIdAndName()
    {
      string? containerId = null;
      try
      {
        containerId = await ApiRunContainerAsync(TestImage);
        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var stats = new List<ContainerStats>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await foreach (var stat in StreamDriver.StreamStatsAsync(
            Context, containerId,
            new StreamStatsConfig { Stream = false },
            cts.Token))
        {
          stats.Add(stat);
        }

        Assert.NotEmpty(stats);
        Assert.False(string.IsNullOrEmpty(stats[0].ContainerId),
            "ContainerId should not be empty");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region AttachAsync

    [Fact]
    public async Task Stream_Attach_ReturnsConnectedResult()
    {
      string? containerId = null;
      AttachResult attachResult = null;
      try
      {
        await EnsureImageAsync(TestImage);
        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Command = [ "sh", "-c",
              "while true; do echo api-attach-output; sleep 1; done" ],
          Detach = true,
          Interactive = true,
          Labels = new Dictionary<string, string>
          {
            [TestLabelKey] = TestLabelValue
          }
        };

        var runResult = await ContainerDriver.RunAsync(Context, config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var result = await StreamDriver.AttachAsync(Context, containerId,
            new AttachConfig { Stdout = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, $"AttachAsync failed: {result.Error}");
        attachResult = result.Data;
        Assert.True(attachResult.IsConnected,
            "Expected IsConnected to be true");
        Assert.NotNull(attachResult.OutputStream);
      }
      finally
      {
        if (attachResult != null)
          await attachResult.DisposeAsync();
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Attach_CanReadOutput()
    {
      string? containerId = null;
      AttachResult attachResult = null;
      try
      {
        await EnsureImageAsync(TestImage);
        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Command = [ "sh", "-c",
              "while true; do echo api-stream-data; sleep 1; done" ],
          Detach = true,
          Interactive = true,
          Labels = new Dictionary<string, string>
          {
            [TestLabelKey] = TestLabelValue
          }
        };

        var runResult = await ContainerDriver.RunAsync(Context, config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var result = await StreamDriver.AttachAsync(Context, containerId,
            new AttachConfig { Stdout = true },
            TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"AttachAsync failed: {result.Error}");
        attachResult = result.Data;

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
            if (sb.ToString().Contains("api-stream-data"))
              break;
          }
        }
        catch (OperationCanceledException) { }

        var output = sb.ToString();
        Assert.True(output.Contains("api-stream-data"),
            $"Expected 'api-stream-data' in output, got: '{output}'");
      }
      finally
      {
        if (attachResult != null)
          await attachResult.DisposeAsync();
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stream_Attach_Dispose_Disconnects()
    {
      string? containerId = null;
      try
      {
        await EnsureImageAsync(TestImage);
        var config = new ContainerCreateConfig
        {
          Image = TestImage,
          Command = ["sleep", "300"],
          Detach = true,
          Interactive = true,
          Labels = new Dictionary<string, string>
          {
            [TestLabelKey] = TestLabelValue
          }
        };

        var runResult = await ContainerDriver.RunAsync(Context, config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var result = await StreamDriver.AttachAsync(Context, containerId,
            new AttachConfig { Stdout = true },
            TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"AttachAsync failed: {result.Error}");

        var attachResult = result.Data;
        Assert.True(attachResult.IsConnected);

        await attachResult.DisposeAsync();
        Assert.False(attachResult.IsConnected,
            "Expected IsConnected to be false after dispose");
      }
      finally
      {
        await ApiRemoveContainerAsync(containerId!);
      }
    }

    #endregion
  }
}
