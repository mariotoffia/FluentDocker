using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiMuxFrameIssueTests
  {
    private static DriverContext Ctx => new("docker-api-mux-frame-test");

    [Fact]
    public async Task StreamLogEntriesAsync_OversizedFrame_ThrowsDriverException()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/containers/ctr/logs", FrameHeader(1, 10 * 1024 * 1024 + 1));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      var error = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(
            Ctx, "ctr", new StreamLogsConfig { Follow = false },
            TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Contains("frame size", error.Message);
    }

    [Fact]
    public async Task ExecAsync_OversizedFrame_ReturnsExecFailed()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-big""}");
      mock.SetupStreamBytes("/exec/exec-big/start", FrameHeader(1, 10 * 1024 * 1024 + 1));
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["echo"], Tty = false },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
      Assert.Contains("frame size", result.Error);
    }

    private static byte[] FrameHeader(byte streamType, int size)
    {
      var frame = new byte[8];
      frame[0] = streamType;
      frame[4] = (byte)((size >> 24) & 0xFF);
      frame[5] = (byte)((size >> 16) & 0xFF);
      frame[6] = (byte)((size >> 8) & 0xFF);
      frame[7] = (byte)(size & 0xFF);
      return frame;
    }
  }
}
