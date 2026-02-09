using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiStreamDriverTests
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
      await foreach (var evt in driver.StreamEventsAsync(Ctx))
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
      await foreach (var evt in driver.StreamEventsAsync(Ctx))
        events.Add(evt);

      Assert.Single(events);
      Assert.Equal("image", events[0].Type);
      Assert.Equal("pull", events[0].Action);
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
      await foreach (var s in driver.StreamStatsAsync(Ctx, "ctr001"))
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
      await foreach (var s in driver.StreamStatsAsync(Ctx, "ctr002"))
        statsList.Add(s);

      Assert.Single(statsList);
      Assert.Equal("ctr002", statsList[0].ContainerId);
    }

    [Fact]
    public async Task StreamStatsAsync_NullContainerId_YieldsNothing()
    {
      var (driver, _) = CreateDriver();

      var statsList = new List<ContainerStats>();
      await foreach (var s in driver.StreamStatsAsync(Ctx, null))
        statsList.Add(s);

      Assert.Empty(statsList);
    }

    #endregion

    #region StreamLogsAsync

    [Fact]
    public async Task StreamLogsAsync_YieldsLinesFromRawStream()
    {
      // The multiplexed reader tries to read an 8-byte header first.
      // When fewer than 8 bytes are available it falls back to raw
      // text mode, yielding lines split on '\n'.
      // Content must be shorter than 8 bytes to trigger the fallback.
      var logContent = "ab\ncd";

      var (driver, mock) = CreateDriver();
      mock.SetupStream("/containers/ctr/logs", logContent);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "ctr",
          new StreamLogsConfig { Follow = false }))
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
      await foreach (var line in driver.StreamLogsAsync(Ctx, "empty"))
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
          new StreamLogsConfig { Follow = false }))
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
          new StreamLogsConfig { Follow = false }))
      {
        lines.Add(line);
      }

      Assert.Equal(2, lines.Count);
      Assert.Equal("first", lines[0]);
      Assert.Equal("second", lines[1]);
    }

    [Fact]
    public async Task StreamLogsAsync_StderrFrame_AlsoYielded()
    {
      var frame = CreateMultiplexedFrame(2, "error output");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux3/logs", frame);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux3",
          new StreamLogsConfig { Follow = false }))
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
          new StreamLogsConfig { Follow = false }))
      {
        lines.Add(line);
      }

      Assert.Equal(3, lines.Count);
      Assert.Equal("line1", lines[0]);
      Assert.Equal("line2", lines[1]);
      Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public async Task StreamLogsAsync_IncompleteHeader_FallsBackToRawText()
    {
      // Only 5 bytes: less than the 8-byte header required.
      // The reader should fall back to raw text mode.
      var bytes = Encoding.UTF8.GetBytes("hello");
      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/mux5/logs", bytes);

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(Ctx, "mux5",
          new StreamLogsConfig { Follow = false }))
      {
        lines.Add(line);
      }

      Assert.Single(lines);
      Assert.Equal("hello", lines[0]);
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
          new StreamLogsConfig { Follow = false }))
      {
        lines.Add(line);
      }

      Assert.Single(lines);
      Assert.Equal("data", lines[0]);
    }

    #endregion

    #region Multiplexed Frame Helpers

    /// <summary>
    /// Creates a Docker multiplexed stream frame.
    /// Header: [stream_type:1][0:3][size:4 big-endian] followed by payload.
    /// </summary>
    private static byte[] CreateMultiplexedFrame(byte streamType, string payload)
    {
      var payloadBytes = Encoding.UTF8.GetBytes(payload);
      var frame = new byte[8 + payloadBytes.Length];
      frame[0] = streamType; // 1=stdout, 2=stderr
                             // bytes 1-3 are zero padding (already zero-initialized)
      frame[4] = (byte)((payloadBytes.Length >> 24) & 0xFF);
      frame[5] = (byte)((payloadBytes.Length >> 16) & 0xFF);
      frame[6] = (byte)((payloadBytes.Length >> 8) & 0xFF);
      frame[7] = (byte)(payloadBytes.Length & 0xFF);
      Array.Copy(payloadBytes, 0, frame, 8, payloadBytes.Length);
      return frame;
    }

    #endregion

    #region AttachAsync

    [Fact]
    public async Task AttachAsync_ReturnsConnectedResult()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/containers/ctr/attach", "attached-stream-data");

      var result = await driver.AttachAsync(Ctx, "ctr");
      Assert.True(result.Success);
      Assert.True(result.Data.IsConnected);
      Assert.NotNull(result.Data.OutputStream);
    }

    [Fact]
    public async Task AttachAsync_Failure_ReturnsErrorResponse()
    {
      // Use a connection that throws on PostStreamAsync to trigger
      // the error handling path in AttachAsync.
      var conn = new ThrowingStreamConnection();
      var driver = new DockerApiStreamDriver(conn);
      driver.Initialize(new DriverContext("docker-api-stream-test"));

      var result = await driver.AttachAsync(Ctx, "fail-ctr");
      Assert.False(result.Success);
      Assert.Contains("Attach failed", result.Error);
      Assert.Equal(ErrorCodes.Container.AttachFailed, result.ErrorCode);
      Assert.NotNull(result.ErrorContext);
      Assert.Contains("/attach", result.ErrorContext.Operation);
    }

    #endregion

    /// <summary>
    /// A mock connection that throws on PostStreamAsync to exercise
    /// the attach failure path.
    /// </summary>
    private sealed class ThrowingStreamConnection : IDockerApiConnection
    {
      public string ApiVersion { get; set; } = "1.45";

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
          throw new NotSupportedException();

      public Task<HttpResponseMessage> PostAsync(
          string path, HttpContent content, CancellationToken ct) =>
          throw new NotSupportedException();

      public Task<HttpResponseMessage> PutAsync(
          string path, HttpContent content, CancellationToken ct) =>
          throw new NotSupportedException();

      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct) =>
          throw new NotSupportedException();

      public Task<Stream> GetStreamAsync(string path, CancellationToken ct) =>
          throw new NotSupportedException();

      public Task<Stream> PostStreamAsync(
          string path, HttpContent content, CancellationToken ct) =>
          throw new InvalidOperationException("simulated stream failure");

      public Task<bool> PingAsync(CancellationToken ct) =>
          Task.FromResult(false);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
