using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public partial class DockerApiStreamDriverTests
  {
    private static DriverContext Ctx => new("docker-api-stream-test");

    private static (DockerApiStreamDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(new DriverContext("docker-api-stream-test"));
      return (driver, mock);
    }

    #region StreamEventsAsync

    [Fact]
    public async Task StreamEventsAsync_ParsesNdjsonEventFields()
    {
      var ndjson =
          @"{""Type"":""container"",""Action"":""start"",""Actor"":{""ID"":""abc123"",""Attributes"":{""name"":""my-app""}},""time"":1700000000,""timeNano"":1700000000000000000,""scope"":""local""}"
          + "\n"
          + @"{""Type"":""network"",""Action"":""connect"",""Actor"":{""ID"":""net456""},""time"":1700000001,""scope"":""local""}";

      var (driver, mock) = CreateDriver();
      mock.SetupStream("/events", ndjson);

      var events = new List<ContainerEvent>();
      await foreach (var evt in driver.StreamEventsAsync(Ctx,
          new StreamEventsConfig { Until = "1700000002" },
          TestContext.Current.CancellationToken))
        events.Add(evt);

      Assert.Equal(2, events.Count);

      Assert.Equal("container", events[0].Type);
      Assert.Equal("start", events[0].Action);
      Assert.Equal("abc123", events[0].ActorId);
      Assert.Equal("my-app", events[0].ActorAttributes["name"]);
      Assert.Equal("local", events[0].Scope);

      Assert.Equal("network", events[1].Type);
      Assert.Equal("connect", events[1].Action);
      Assert.Equal("net456", events[1].ActorId);
    }

    [Fact]
    public async Task StreamEventsAsync_SkipsMalformedLines()
    {
      var ndjson = "not-valid-json\n"
          + @"{""Type"":""image"",""Action"":""pull"",""Actor"":{""ID"":""img789""},""time"":0}";

      var (driver, mock) = CreateDriver();
      mock.SetupStream("/events", ndjson);

      var events = new List<ContainerEvent>();
      await foreach (var evt in driver.StreamEventsAsync(Ctx,
          new StreamEventsConfig { Until = "1" },
          TestContext.Current.CancellationToken))
        events.Add(evt);

      Assert.Single(events);
      Assert.Equal("image", events[0].Type);
      Assert.Equal("pull", events[0].Action);
    }

    [Fact]
    public async Task StreamEventsAsync_WhenBoundlessStreamEnds_ThrowsStreamEndedAfterYieldingEvents()
    {
      var ndjson =
          @"{""Type"":""container"",""Action"":""start"",""Actor"":{""ID"":""abc123""},""time"":1700000000}"
          + "\n";
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/events", ndjson);
      var events = new List<ContainerEvent>();

      var error = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var evt in driver.StreamEventsAsync(Ctx,
            new StreamEventsConfig(), TestContext.Current.CancellationToken))
        {
          events.Add(evt);
        }
      });

      Assert.Single(events);
      Assert.Equal(ErrorCodes.Api.StreamEnded, error.ErrorCode);
    }

    [Fact]
    public async Task StreamEventsAsync_WhenUntilIsSet_CompletesAfterEvents()
    {
      var ndjson =
          @"{""Type"":""container"",""Action"":""die"",""Actor"":{""ID"":""abc123""},""time"":1700000000}"
          + "\n";
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/events", ndjson);

      var events = new List<ContainerEvent>();
      await foreach (var evt in driver.StreamEventsAsync(Ctx,
          new StreamEventsConfig { Until = "1700000001" },
          TestContext.Current.CancellationToken))
      {
        events.Add(evt);
      }

      Assert.Single(events);
      Assert.Equal("die", events[0].Action);
    }

    [Fact]
    public async Task StreamEventsAsync_WhenStreamReadFails_ThrowsDriverException()
    {
      var prefix = Encoding.UTF8.GetBytes(
          @"{""Type"":""container"",""Action"":""start"",""Actor"":{""ID"":""abc123""},""time"":1700000000}");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamReadThrows("/events", prefix, new IOException("reset"));

      var error = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamEventsAsync(Ctx,
            new StreamEventsConfig(), TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.Api.StreamInterrupted, error.ErrorCode);
    }

    #endregion

    #region StreamStatsAsync

    [Fact]
    public async Task StreamStatsAsync_ParsesStatsJson()
    {
      var statsJson =
          @"{""id"":""ctr001"",""name"":""/web-server"",""read"":""2024-01-15T10:30:00Z"","
          + @"""cpu_stats"":{""cpu_usage"":{""total_usage"":500000},""system_cpu_usage"":10000000,""online_cpus"":4},"
          + @"""precpu_stats"":{""cpu_usage"":{""total_usage"":400000},""system_cpu_usage"":9000000},"
          + @"""memory_stats"":{""usage"":104857600,""limit"":1073741824},"
          + @"""networks"":{""eth0"":{""rx_bytes"":1024,""tx_bytes"":2048},""eth1"":{""rx_bytes"":512,""tx_bytes"":256}},"
          + @"""pids_stats"":{""current"":15}}";

      var (driver, mock) = CreateDriver();
      mock.SetupStream("/stats", statsJson);

      var statsList = new List<ContainerStats>();
      await foreach (var s in driver.StreamStatsAsync(Ctx, "ctr001", cancellationToken: TestContext.Current.CancellationToken))
        statsList.Add(s);

      Assert.Single(statsList);
      var stats = statsList[0];

      Assert.Equal("ctr001", stats.ContainerId);
      Assert.Equal("web-server", stats.Name);
      Assert.True(stats.CpuPercentage > 0, "CPU percentage should be computed");
      Assert.Equal(104857600L, stats.MemoryUsage);
      Assert.Equal(1073741824L, stats.MemoryLimit);
      Assert.True(stats.MemoryPercentage > 0);
      Assert.Equal(1536L, stats.NetworkRx); // 1024 + 512
      Assert.Equal(2304L, stats.NetworkTx); // 2048 + 256
      Assert.Equal(15, stats.Pids);
    }

    [Fact]
    public async Task StreamStatsAsync_SkipsMalformedLines()
    {
      var ndjson = "bad-json\n"
          + @"{""id"":""ctr002"",""name"":""/db"","
          + @"""memory_stats"":{""usage"":50000,""limit"":100000},"
          + @"""pids_stats"":{""current"":3}}";

      var (driver, mock) = CreateDriver();
      mock.SetupStream("/stats", ndjson);

      var statsList = new List<ContainerStats>();
      await foreach (var s in driver.StreamStatsAsync(Ctx, "ctr002", cancellationToken: TestContext.Current.CancellationToken))
        statsList.Add(s);

      Assert.Single(statsList);
      Assert.Equal("ctr002", statsList[0].ContainerId);
    }

    [Fact]
    public async Task StreamStatsAsync_NullContainerId_ThrowsArgumentException()
    {
      var (driver, _) = CreateDriver();

      await Assert.ThrowsAsync<ArgumentException>(async () =>
      {
        await foreach (var _ in driver.StreamStatsAsync(
            Ctx, null!, cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });
    }

    #endregion

    #region StreamLogsAsync

    [Fact]
    public async Task StreamLogsAsync_YieldsLinesFromRawStream()
    {
      var logContent = "ab\ncd";

      var (driver, mock) = CreateDriver();
      mock.SetupGet("/containers/raw/json", 200, "{\"Config\":{\"Tty\":true}}");
      mock.SetupStream("/containers/raw/logs", logContent);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "raw",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Contains("ab", lines);
      Assert.Contains("cd", lines);
    }

    [Fact]
    public async Task StreamLogsAsync_EmptyStream_YieldsNothing()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/containers/empty/logs", "");

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "empty", cancellationToken: TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Empty(lines);
    }

    [Fact]
    public async Task StreamLogsAsync_MultiplexedStdoutFrame_YieldsContent()
    {
      var frame = CreateMultiplexedFrame(1, "hello");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux1/logs", frame);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux1",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Single(lines);
      Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public async Task StreamLogsAsync_MultipleFrames_YieldsAllContent()
    {
      var frame1 = CreateMultiplexedFrame(1, "first");
      var frame2 = CreateMultiplexedFrame(1, "second");
      var combined = new byte[frame1.Length + frame2.Length];
      Array.Copy(frame1, 0, combined, 0, frame1.Length);
      Array.Copy(frame2, 0, combined, frame1.Length, frame2.Length);

      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux2/logs", combined);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux2",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      var only = Assert.Single(lines);
      Assert.Equal("firstsecond", only);
    }

    [Fact]
    public async Task StreamLogsAsync_StderrFrame_AlsoYielded()
    {
      var frame = CreateMultiplexedFrame(2, "error output");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux3/logs", frame);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux3",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Single(lines);
      Assert.Equal("error output", lines[0]);
    }

    [Fact]
    public async Task StreamLogsAsync_FrameWithNewlines_SplitsIntoLines()
    {
      var frame = CreateMultiplexedFrame(1, "line1\nline2\nline3");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux4/logs", frame);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux4",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Equal(3, lines.Count);
      Assert.Equal("line1", lines[0]);
      Assert.Equal("line2", lines[1]);
      Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public async Task StreamLogsAsync_ShortRawPayloadWithFailedTtyDetect_YieldsLine()
    {
      var bytes = Encoding.UTF8.GetBytes("hello");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux5/logs", bytes);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux5",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Equal(["hello"], lines);
    }

    [Fact]
    public async Task StreamLogsAsync_ZeroSizeFrame_StopsReading()
    {
      // Build a valid frame followed by a zero-size frame.
      // The reader should yield the first frame's content and then stop.
      var goodFrame = CreateMultiplexedFrame(1, "data");
      var zeroFrame = new byte[8]; // all zeros => stream_type=0, size=0
      zeroFrame[0] = 1; // stdout type, but size stays 0

      var combined = new byte[goodFrame.Length + zeroFrame.Length];
      Array.Copy(goodFrame, 0, combined, 0, goodFrame.Length);
      Array.Copy(zeroFrame, 0, combined, goodFrame.Length, zeroFrame.Length);

      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux6/logs", combined);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux6",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Single(lines);
      Assert.Equal("data", lines[0]);
    }

    #endregion

  }
}
