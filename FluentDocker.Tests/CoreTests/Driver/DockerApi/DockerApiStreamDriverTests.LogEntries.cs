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
  /// <summary>
  /// Source-tagged log-entry streaming, TTY detection, stream-failure surfacing,
  /// and Attach tests. Partial of <see cref="DockerApiStreamDriverTests"/>.
  /// </summary>
  public partial class DockerApiStreamDriverTests
  {
    #region StreamLogEntriesAsync (source tagging, issue #326)

    [Fact]
    public async Task StreamLogEntriesAsync_TagsStdoutAndStderrPerFrame()
    {
      var f1 = CreateMultiplexedFrame(1, "out-line");
      var f2 = CreateMultiplexedFrame(2, "err-line");
      var combined = new byte[f1.Length + f2.Length];
      Array.Copy(f1, 0, combined, 0, f1.Length);
      Array.Copy(f2, 0, combined, f1.Length, f2.Length);

      var (driver, mock) = CreateDriver();
      mock.SetupStreamBytes("/containers/src1/logs", combined);

      var entries = new List<LogEntry>();
      await foreach (var entry in driver.StreamLogEntriesAsync(Ctx, "src1",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        entries.Add(entry);
      }

      Assert.Equal(2, entries.Count);
      Assert.Equal(LogStreamSource.Stdout, entries[0].Source);
      Assert.Equal("out-line", entries[0].Line);
      Assert.Equal(LogStreamSource.Stderr, entries[1].Source);
      Assert.Equal("err-line", entries[1].Line);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_RawTtyStream_DefaultsToStdout()
    {
      // Fewer than 8 bytes triggers the raw/TTY fallback path.
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/containers/src2/logs", "ab\ncd");

      var entries = new List<LogEntry>();
      await foreach (var entry in driver.StreamLogEntriesAsync(Ctx, "src2",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        entries.Add(entry);
      }

      Assert.NotEmpty(entries);
      Assert.All(entries, e => Assert.Equal(LogStreamSource.Stdout, e.Source));
    }

    #endregion

    #region A7 / A1 - Stream open failure surfaces (no silent empty)

    [Fact]
    public async Task StreamLogEntriesAsync_StreamOpenThrows_PropagatesDriverException()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStreamThrows("/logs", new HttpRequestException("logs daemon down"));

      await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(Ctx, "ctr",
            new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });
    }

    [Fact]
    public async Task StreamEventsAsync_StreamOpenThrows_PropagatesDriverException()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStreamThrows("/events", new HttpRequestException("events daemon down"));

      await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamEventsAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });
    }

    #endregion

    #region M5 - StreamEventsConfig.Filters are applied

    [Fact]
    public async Task StreamEventsAsync_CustomFilters_AreMergedIntoQuery()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/events", "");

      var config = new StreamEventsConfig
      {
        Types = { "container" },
        Filters = { ["label"] = "foo" }
      };

      await foreach (var _ in driver.StreamEventsAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      var req = mock.GetRequests().First(
          r => r.Method == "GET_STREAM" && r.Path.Contains("/events"));
      Assert.Contains("filters=", req.Path);
      Assert.Contains("type", req.Path);     // from Types
      Assert.Contains("container", req.Path); // from Types value
      Assert.Contains("label", req.Path);    // custom filter key
      Assert.Contains("foo", req.Path);      // custom filter value
    }

    #endregion

    #region M1 - TTY raw stream is not misparsed as multiplexed

    [Fact]
    public async Task StreamLogEntriesAsync_TtyContainer_YieldsRawText()
    {
      var (driver, mock) = CreateDriver();
      // Tty=true => the log stream is raw text, not 8-byte multiplex frames.
      mock.SetupGet("/containers/ttyc/json", 200, "{\"Config\":{\"Tty\":true}}");
      // A raw payload whose leading bytes would be misread as a multiplex header.
      var raw = Encoding.UTF8.GetBytes("hello world\nsecond line\n");
      mock.SetupStreamBytes("/containers/ttyc/logs", raw);

      var entries = new List<LogEntry>();
      await foreach (var e in driver.StreamLogEntriesAsync(Ctx, "ttyc",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        entries.Add(e);
      }

      Assert.Equal(2, entries.Count);
      Assert.Equal("hello world", entries[0].Line);
      Assert.Equal("second line", entries[1].Line);
      Assert.All(entries, e => Assert.Equal(LogStreamSource.Stdout, e.Source));
    }

    [Fact]
    public async Task StreamLogEntriesAsync_NonTtyContainer_StillDemultiplexes()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/containers/muxc/json", 200, "{\"Config\":{\"Tty\":false}}");
      var frame = CreateMultiplexedFrame(2, "err-line");
      mock.SetupStreamBytes("/containers/muxc/logs", frame);

      var entries = new List<LogEntry>();
      await foreach (var e in driver.StreamLogEntriesAsync(Ctx, "muxc",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        entries.Add(e);
      }

      Assert.Single(entries);
      Assert.Equal("err-line", entries[0].Line);
      Assert.Equal(LogStreamSource.Stderr, entries[0].Source);
    }

    #endregion

    #region F4 / F5 - Multiplexed read failure surfaces; raw-header self-correction

    [Fact]
    public async Task StreamLogEntriesAsync_StreamThrowsMidRead_PropagatesDriverException()
    {
      var (driver, mock) = CreateDriver();
      var frame = CreateMultiplexedFrame(1, "hello");
      mock.SetupStreamReadThrows("/containers/midfail/logs", frame,
          new IOException("connection reset by peer"));

      await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(Ctx, "midfail",
            new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });
    }

    [Fact]
    public async Task StreamLogEntriesAsync_ShortPayload_ThrowsDriverException()
    {
      var (driver, mock) = CreateDriver();
      var frame = CreateMultiplexedFrame(1, "hello");
      mock.SetupStreamBytes("/containers/short/logs", frame[..^2]);

      var error = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(Ctx, "short",
            new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });
      Assert.Contains("truncated", error.Message);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_RawHeaderWithFailedTtyDetect_SelfCorrectsToRawText()
    {
      var (driver, mock) = CreateDriver();
      // No /json setup => TTY detection fails and defaults to demux. The payload is raw
      // text whose first 8 bytes ("hello wo") are not a valid multiplex header, so the
      // reader must self-correct and emit raw stdout lines (F5).
      var raw = Encoding.UTF8.GetBytes("hello world\nsecond line\n");
      mock.SetupStreamBytes("/containers/rawhdr/logs", raw);

      var entries = new List<LogEntry>();
      await foreach (var e in driver.StreamLogEntriesAsync(Ctx, "rawhdr",
          new StreamLogsConfig { Follow = false }, cancellationToken: TestContext.Current.CancellationToken))
      {
        entries.Add(e);
      }

      Assert.Equal(2, entries.Count);
      Assert.Equal("hello world", entries[0].Line);
      Assert.Equal("second line", entries[1].Line);
      Assert.All(entries, e => Assert.Equal(LogStreamSource.Stdout, e.Source));
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

      var result = await driver.AttachAsync(Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken);
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

      var result = await driver.AttachAsync(Ctx, "fail-ctr", cancellationToken: TestContext.Current.CancellationToken);
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

      public Task<Stream> PostStreamAsync(
          string path, HttpContent content,
          IReadOnlyDictionary<string, string> headers, CancellationToken ct) =>
          throw new InvalidOperationException("simulated stream failure");

      public Task<bool> PingAsync(CancellationToken ct) =>
          Task.FromResult(false);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
