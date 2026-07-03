using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiStreamDriverDemuxTests
  {
    private static DriverContext Ctx => new("docker-api-stream-demux-test");

    [Fact]
    public async Task StreamLogEntriesAsync_RepeatedIncompleteUtf8Frames_EmitsBeforeStreamEnds()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamReadThrows("/containers/ctr/logs", Combine(
          Frame(1, [0xF0]),
          Frame(1, [0xF0]),
          Frame(1, [0xF0])),
          new IOException("stream still open"));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var entry in driver.StreamLogEntriesAsync(
            Ctx, "ctr", new StreamLogsConfig { Follow = true },
            TestContext.Current.CancellationToken))
        {
          entries.Add(entry);
        }
      });

      Assert.Contains("stream still open", ex.Message);
      Assert.NotEmpty(entries);
      Assert.All(entries, static entry => Assert.Equal(LogStreamSource.Stdout, entry.Source));
    }

    private static byte[] Frame(byte streamType, byte[] payload)
    {
      var frame = new byte[8 + payload.Length];
      frame[0] = streamType;
      frame[4] = (byte)((payload.Length >> 24) & 0xFF);
      frame[5] = (byte)((payload.Length >> 16) & 0xFF);
      frame[6] = (byte)((payload.Length >> 8) & 0xFF);
      frame[7] = (byte)(payload.Length & 0xFF);
      Array.Copy(payload, 0, frame, 8, payload.Length);
      return frame;
    }

    private static byte[] Combine(params byte[][] frames)
    {
      var combined = new byte[frames.Sum(static f => f.Length)];
      var offset = 0;
      foreach (var frame in frames)
      {
        Array.Copy(frame, 0, combined, offset, frame.Length);
        offset += frame.Length;
      }
      return combined;
    }
  }
}
