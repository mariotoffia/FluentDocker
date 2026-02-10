using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiContainerOperationsTests
  {
    private static DriverContext Ctx => new("docker-api-ops-test");

    private static DockerApiContainerDriver CreateDriver(MockDockerApiConnection conn)
    {
      var driver = new DockerApiContainerDriver(conn);
      driver.Initialize(new DriverContext("docker-api-ops-test"));
      return driver;
    }

    // ── GetLogsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_ReturnsStreamContent()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/logs", "hello from container\n");
      var driver = CreateDriver(mock);

      var result = await driver.GetLogsAsync(Ctx, "ctr1");

      Assert.True(result.Success);
      Assert.Contains("hello from container", result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_WithTimestamps_IncludesTimestampParam()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/logs", "2025-01-01T00:00:00Z log line\n");
      var driver = CreateDriver(mock);

      var result = await driver.GetLogsAsync(Ctx, "ctr1", timestamps: true);

      Assert.True(result.Success);
      var req = mock.GetRequests().First(r => r.Method == "GET_STREAM");
      Assert.Contains("timestamps=true", req.Path);
    }

    [Fact]
    public async Task GetLogsAsync_FollowIsFalseByDefault()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/logs", "some logs");
      var driver = CreateDriver(mock);

      await driver.GetLogsAsync(Ctx, "ctr1");

      var req = mock.GetRequests().First(r => r.Method == "GET_STREAM");
      Assert.Contains("follow=false", req.Path);
    }

    [Fact]
    public async Task GetLogsAsync_WithTail_IncludesTailParam()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/logs", "last 10 lines");
      var driver = CreateDriver(mock);

      await driver.GetLogsAsync(Ctx, "ctr1", tail: 10);

      var req = mock.GetRequests().First(r => r.Method == "GET_STREAM");
      Assert.Contains("tail=10", req.Path);
    }

    // ── TopAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task TopAsync_ParsesTitlesAndProcesses()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/top", 200,
          @"{""Titles"":[""PID"",""CMD""],""Processes"":[[""1"",""sleep""]]}");
      var driver = CreateDriver(mock);

      var result = await driver.TopAsync(Ctx, "ctr1");

      Assert.True(result.Success);
      Assert.Equal(new List<string> { "PID", "CMD" }, result.Data.Titles);
      Assert.Single(result.Data.Processes);
      Assert.Equal(new List<string> { "1", "sleep" }, result.Data.Processes[0]);
    }

    [Fact]
    public async Task TopAsync_WithPsOptions_UrlEncodesParam()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/top", 200,
          @"{""Titles"":[""PID""],""Processes"":[[""1""]]}");
      var driver = CreateDriver(mock);

      await driver.TopAsync(Ctx, "ctr1", psOptions: "aux --sort=-rss");

      var req = mock.GetRequests().First(r => r.Method == "GET");
      Assert.Contains("ps_args=", req.Path);
      // Space is URL-encoded as + or %20
      Assert.DoesNotContain("aux --sort", req.Path);
    }

    [Fact]
    public async Task TopAsync_404_ReturnsTopFailedErrorCode()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/top", 404, @"{""message"":""no such container""}");
      var driver = CreateDriver(mock);

      var result = await driver.TopAsync(Ctx, "gone");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.TopFailed, result.ErrorCode);
    }

    // ── DiffAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DiffAsync_ParsesChangesWithKindMapping()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/changes", 200,
          @"[{""Path"":""/tmp/file"",""Kind"":1},"
          + @"{""Path"":""/var/log"",""Kind"":0},"
          + @"{""Path"":""/old"",""Kind"":2}]");
      var driver = CreateDriver(mock);

      var result = await driver.DiffAsync(Ctx, "ctr1");

      Assert.True(result.Success);
      Assert.Equal(3, result.Data.Count);
      // Kind 1 = Added (A)
      Assert.Equal("/tmp/file", result.Data[0].Path);
      Assert.Equal("A", result.Data[0].Kind);
      // Kind 0 = Modified (C)
      Assert.Equal("/var/log", result.Data[1].Path);
      Assert.Equal("C", result.Data[1].Kind);
      // Kind 2 = Deleted (D)
      Assert.Equal("/old", result.Data[2].Path);
      Assert.Equal("D", result.Data[2].Kind);
    }

    [Fact]
    public async Task DiffAsync_EmptyChanges_ReturnsEmptyList()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/changes", 200, "[]");
      var driver = CreateDriver(mock);

      var result = await driver.DiffAsync(Ctx, "ctr1");

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    // ── ExecAsync ───────────────────────────────────────────────────

    /// <summary>Creates a Docker multiplexed frame (8-byte header + payload).</summary>
    private static byte[] CreateMuxFrame(byte streamType, string text)
    {
      var payload = System.Text.Encoding.UTF8.GetBytes(text);
      var frame = new byte[8 + payload.Length];
      frame[0] = streamType;
      frame[4] = (byte)((payload.Length >> 24) & 0xFF);
      frame[5] = (byte)((payload.Length >> 16) & 0xFF);
      frame[6] = (byte)((payload.Length >> 8) & 0xFF);
      frame[7] = (byte)(payload.Length & 0xFF);
      System.Array.Copy(payload, 0, frame, 8, payload.Length);
      return frame;
    }

    [Fact]
    public async Task ExecAsync_TtyMode_ReturnsRawOutput()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-tty""}");
      mock.SetupStream("/exec/exec-tty/start", "raw tty output");
      mock.SetupGet("/exec/exec-tty/json", 200, @"{""ExitCode"":0,""Running"":false}");
      var driver = CreateDriver(mock);

      var config = new ExecConfig { Command = new[] { "echo", "hello" }, Tty = true };
      var result = await driver.ExecAsync(Ctx, "ctr1", config);

      Assert.True(result.Success);
      Assert.Equal(0, result.Data.ExitCode);
      Assert.Equal("raw tty output", result.Data.StdOut);
      Assert.Equal(string.Empty, result.Data.StdErr);
    }

    [Fact]
    public async Task ExecAsync_NonTtyMode_DemultiplexesStdoutAndStderr()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-mux""}");

      // Build multiplexed frames: stdout then stderr
      var stdoutFrame = CreateMuxFrame(1, "hello stdout");
      var stderrFrame = CreateMuxFrame(2, "hello stderr");
      var combined = new byte[stdoutFrame.Length + stderrFrame.Length];
      stdoutFrame.CopyTo(combined, 0);
      stderrFrame.CopyTo(combined, stdoutFrame.Length);
      mock.SetupStreamBytes("/exec/exec-mux/start", combined);
      mock.SetupGet("/exec/exec-mux/json", 200, @"{""ExitCode"":0}");
      var driver = CreateDriver(mock);

      var config = new ExecConfig { Command = new[] { "sh", "-c", "echo" }, Tty = false };
      var result = await driver.ExecAsync(Ctx, "ctr1", config);

      Assert.True(result.Success);
      Assert.Equal("hello stdout", result.Data.StdOut);
      Assert.Equal("hello stderr", result.Data.StdErr);
    }

    [Fact]
    public async Task ExecAsync_NonTtyMode_MixedFrames_SeparatesCorrectly()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-mix""}");

      var f1 = CreateMuxFrame(1, "out1 ");
      var f2 = CreateMuxFrame(2, "err1 ");
      var f3 = CreateMuxFrame(1, "out2");
      var combined = new byte[f1.Length + f2.Length + f3.Length];
      f1.CopyTo(combined, 0);
      f2.CopyTo(combined, f1.Length);
      f3.CopyTo(combined, f1.Length + f2.Length);
      mock.SetupStreamBytes("/exec/exec-mix/start", combined);
      mock.SetupGet("/exec/exec-mix/json", 200, @"{""ExitCode"":1}");
      var driver = CreateDriver(mock);

      var config = new ExecConfig { Command = new[] { "test" }, Tty = false };
      var result = await driver.ExecAsync(Ctx, "ctr1", config);

      Assert.True(result.Success);
      Assert.Equal(1, result.Data.ExitCode);
      Assert.Equal("out1 out2", result.Data.StdOut);
      Assert.Equal("err1 ", result.Data.StdErr);
    }

    [Fact]
    public async Task ExecAsync_NonTtyMode_EmptyStream_ReturnsEmptyStrings()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-empty""}");
      mock.SetupStreamBytes("/exec/exec-empty/start", new byte[0]);
      mock.SetupGet("/exec/exec-empty/json", 200, @"{""ExitCode"":0}");
      var driver = CreateDriver(mock);

      var config = new ExecConfig { Command = new[] { "true" }, Tty = false };
      var result = await driver.ExecAsync(Ctx, "ctr1", config);

      Assert.True(result.Success);
      Assert.Equal(string.Empty, result.Data.StdOut);
      Assert.Equal(string.Empty, result.Data.StdErr);
    }

    [Fact]
    public async Task ExecAsync_WithEnvironment_FormatsAsKeyValueArray()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""execEnv""}");
      mock.SetupStream("/exec/execEnv/start", "");
      mock.SetupGet("/exec/execEnv/json", 200, @"{""ExitCode"":0}");
      var driver = CreateDriver(mock);

      var config = new ExecConfig
      {
        Command = new[] { "env" },
        Environment = new Dictionary<string, string>
        {
          ["MY_VAR"] = "hello",
          ["OTHER"] = "world"
        }
      };
      await driver.ExecAsync(Ctx, "ctr1", config);

      // Verify the POST body for exec create contains KEY=VALUE format
      var createReq = mock.GetRequests()
          .First(r => r.Method == "POST" && r.Path.Contains("/exec"));
      Assert.Contains("MY_VAR=hello", createReq.Body);
      Assert.Contains("OTHER=world", createReq.Body);
    }

    [Fact]
    public async Task ExecAsync_EmptyExecId_ReturnsExecFailedError()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""""}");
      var driver = CreateDriver(mock);

      var config = new ExecConfig { Command = new[] { "ls" } };
      var result = await driver.ExecAsync(Ctx, "ctr1", config);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
      Assert.Contains("empty ID", result.Error);
    }

    // ── CopyToAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CopyToAsync_FileNotExists_ReturnsInvalidArgument()
    {
      var mock = new MockDockerApiConnection();
      var driver = CreateDriver(mock);

      var result = await driver.CopyToAsync(
          Ctx, "ctr1",
          "/nonexistent/path/to/file.txt",
          "/container/dest");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
      Assert.Contains("does not exist", result.Error);
    }

    // ── RenameAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_IncludesNameParam()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/rename", 204, "{}");
      var driver = CreateDriver(mock);

      var result = await driver.RenameAsync(Ctx, "ctr1", "new-name");

      Assert.True(result.Success);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("name=new-name", req.Path);
    }

    [Fact]
    public async Task RenameAsync_404_ReturnsRenameFailedErrorCode()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/rename", 404, @"{""message"":""no such container""}");
      var driver = CreateDriver(mock);

      var result = await driver.RenameAsync(Ctx, "ghost", "new-name");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.RenameFailed, result.ErrorCode);
    }

    // ── UpdateAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_SendsResourceLimitsInBody()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/update", 200, "{}");
      var driver = CreateDriver(mock);

      var config = new ContainerUpdateConfig
      {
        MemoryLimit = 536870912,
        CpuShares = 512,
        CpuPeriod = 100000,
        CpuQuota = 50000,
        CpusetCpus = "0-3",
        PidsLimit = 100
      };
      var result = await driver.UpdateAsync(Ctx, "ctr1", config);

      Assert.True(result.Success);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("536870912", req.Body);
      Assert.Contains("512", req.Body);
      Assert.Contains("100000", req.Body);
      Assert.Contains("50000", req.Body);
      Assert.Contains("0-3", req.Body);
      Assert.Contains("100", req.Body);
    }

    [Fact]
    public async Task UpdateAsync_WithRestartPolicy_IncludesPolicyInBody()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/update", 200, "{}");
      var driver = CreateDriver(mock);

      var config = new ContainerUpdateConfig
      {
        RestartPolicy = "on-failure"
      };
      var result = await driver.UpdateAsync(Ctx, "ctr1", config);

      Assert.True(result.Success);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("on-failure", req.Body);
      Assert.Contains("RestartPolicy", req.Body);
    }

    [Fact]
    public async Task UpdateAsync_404_ReturnsUpdateFailedErrorCode()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/update", 404, @"{""message"":""no such container""}");
      var driver = CreateDriver(mock);

      var config = new ContainerUpdateConfig { MemoryLimit = 1024 };
      var result = await driver.UpdateAsync(Ctx, "ghost", config);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.UpdateFailed, result.ErrorCode);
    }
  }
}
