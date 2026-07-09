using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public partial class DockerApiContainerOperationsTests
  {
    [Fact]
    public async Task ExecAsync_WhenExitCodeInitiallyUnavailable_ReinspectsAndPreservesOutput()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-race""}");
      var stdoutFrame = CreateMuxFrame(1, "captured stdout");
      var stderrFrame = CreateMuxFrame(2, "captured stderr");
      mock.SetupStreamBytes("/exec/exec-race/start", stdoutFrame.Concat(stderrFrame).ToArray());
      mock.SetupGetSequence("/exec/exec-race/json",
          (200, @"{""Running"":true}"),
          (200, @"{""Running"":false,""ExitCode"":0}"));
      var driver = CreateDriver(mock);

      var result = await driver.ExecAsync(Ctx, "ctr1",
          new ExecConfig { Command = ["sh", "-c", "echo"], Tty = false },
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(0, result.Data.ExitCode);
      Assert.Equal("captured stdout", result.Data.StdOut);
      Assert.Equal("captured stderr", result.Data.StdErr);
      Assert.True(mock.GetRequests().Count(r => r.Path.Contains("/exec/exec-race/json")) >= 2);
    }

    [Fact]
    public async Task ExecAsync_WhenExitCodeNeverAvailable_FailsWithCapturedOutputInContext()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-running""}");
      var stdoutFrame = CreateMuxFrame(1, "lost stdout");
      var stderrFrame = CreateMuxFrame(2, "lost stderr");
      mock.SetupStreamBytes("/exec/exec-running/start", stdoutFrame.Concat(stderrFrame).ToArray());
      mock.SetupGetSequence("/exec/exec-running/json",
          (200, @"{""Running"":true}"),
          (200, @"{""Running"":true}"),
          (200, @"{""Running"":true}"),
          (200, @"{""Running"":true}"),
          (200, @"{""Running"":true}"));
      var driver = CreateDriver(mock);
      var context = new DriverContext("docker-api-ops-test")
      {
        RequestTimeout = TimeSpan.FromMilliseconds(250)
      };
      var sw = Stopwatch.StartNew();

      var result = await driver.ExecAsync(context, "ctr1",
          new ExecConfig { Command = ["sh", "-c", "echo"], Tty = false },
          cancellationToken: TestContext.Current.CancellationToken);

      sw.Stop();
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
      Assert.Contains("lost stdout", result.ErrorContext.StdOut);
      Assert.Contains("lost stderr", result.ErrorContext.StdOut);
      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }
  }
}
