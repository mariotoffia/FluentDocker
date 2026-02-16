using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiContainerDriverTests
  {
    private static DriverContext Ctx => new("docker-api-test");

    private static (DockerApiContainerDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(new DriverContext("docker-api-test"));
      return (driver, mock);
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsContainerId()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""abc123"",""Warnings"":[]}");

      var result = await driver.CreateAsync(Ctx, new ContainerCreateConfig { Image = "alpine" }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("abc123", result.Data.Id);
      Assert.Empty(result.Data.Warnings);
    }

    [Fact]
    public async Task CreateAsync_WithName_IncludesNameQueryParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""def456"",""Warnings"":[]}");

      var result = await driver.CreateAsync(
          Ctx, new ContainerCreateConfig { Image = "alpine", Name = "my-container" }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("def456", result.Data.Id);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("?name=my-container", req.Path);
    }

    [Fact]
    public async Task CreateAsync_SendsImageInRequestBody()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""x"",""Warnings"":[]}");

      await driver.CreateAsync(Ctx, new ContainerCreateConfig { Image = "nginx:latest" }, cancellationToken: TestContext.Current.CancellationToken);

      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("nginx:latest", req.Body);
    }

    [Fact]
    public async Task CreateAsync_404_ReturnsCreateFailedErrorCode()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/containers/create", 404, @"{""message"":""no such image""}");

      var result = await driver.CreateAsync(
          Ctx, new ContainerCreateConfig { Image = "nonexistent" }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.CreateFailed, result.ErrorCode);
      Assert.Contains("no such image", result.Error);
    }

    // ── StartAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/start", 204, "{}");
      Assert.True((await driver.StartAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    // ── StopAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/stop", 204, "{}");
      Assert.True((await driver.StopAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task StopAsync_WithTimeout_IncludesTimeoutParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/stop", 204, "{}");

      Assert.True((await driver.StopAsync(Ctx, "abc123", timeout: 30, cancellationToken: TestContext.Current.CancellationToken)).Success);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("?t=30", req.Path);
    }

    // ── RestartAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RestartAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/restart", 204, "{}");
      Assert.True((await driver.RestartAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task RestartAsync_WithTimeout_IncludesTimeoutParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/restart", 204, "{}");

      await driver.RestartAsync(Ctx, "abc123", timeout: 15, cancellationToken: TestContext.Current.CancellationToken);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("?t=15", req.Path);
    }

    // ── PauseAsync / UnpauseAsync ───────────────────────────────────

    [Fact]
    public async Task PauseAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/pause", 204, "{}");
      Assert.True((await driver.PauseAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task UnpauseAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/unpause", 204, "{}");
      Assert.True((await driver.UnpauseAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    // ── KillAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task KillAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/kill", 204, "{}");
      Assert.True((await driver.KillAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task KillAsync_IncludesSignalParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/kill", 204, "{}");

      Assert.True((await driver.KillAsync(Ctx, "abc123", "SIGTERM", cancellationToken: TestContext.Current.CancellationToken)).Success);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("?signal=SIGTERM", req.Path);
    }

    [Fact]
    public async Task KillAsync_DefaultSignal_IsSIGKILL()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/kill", 204, "{}");

      await driver.KillAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken);
      var req = mock.GetRequests().First(r => r.Method == "POST");
      Assert.Contains("signal=SIGKILL", req.Path);
    }

    // ── RemoveAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/containers/", 204, "{}");
      Assert.True((await driver.RemoveAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task RemoveAsync_IncludesForceAndVolumeParams()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/containers/", 204, "{}");

      Assert.True((await driver.RemoveAsync(Ctx, "abc123", force: true, removeVolumes: true, cancellationToken: TestContext.Current.CancellationToken)).Success);
      var req = mock.GetRequests().First(r => r.Method == "DELETE");
      Assert.Contains("force=true", req.Path);
      Assert.Contains("v=true", req.Path);
    }

    [Fact]
    public async Task RemoveAsync_DefaultsForceAndVolumeToFalse()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/containers/", 204, "{}");

      await driver.RemoveAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken);
      var req = mock.GetRequests().First(r => r.Method == "DELETE");
      Assert.Contains("force=false", req.Path);
      Assert.Contains("v=false", req.Path);
    }

    // ── WaitAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task WaitAsync_ReturnsExitCode()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/wait", 200, @"{""StatusCode"":0}");

      var result = await driver.WaitAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(0, result.Data.ExitCode);
      Assert.Null(result.Data.Error);
    }

    [Fact]
    public async Task WaitAsync_ReturnsNonZeroExitCodeWithError()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/wait", 200,
          @"{""StatusCode"":137,""Error"":{""Message"":""OOM killed""}}");

      var result = await driver.WaitAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(137, result.Data.ExitCode);
      Assert.Equal("OOM killed", result.Data.Error);
    }

    // ── InspectAsync ────────────────────────────────────────────────

    [Fact]
    public async Task InspectAsync_ParsesFullContainerJson()
    {
      const string json =
          @"{""Id"":""abc123fullhash"",""Name"":""/my-container"","
          + @"""Image"":""sha256:abc"",""Driver"":""overlay2"","
          + @"""State"":{""Status"":""running"",""Running"":true,""Paused"":false,"
          + @"""Restarting"":false,""OOMKilled"":false,""Dead"":false,"
          + @"""Pid"":12345,""ExitCode"":0},"
          + @"""Config"":{""Hostname"":""abc123"",""Image"":""alpine:latest"","
          + @"""Tty"":true,""OpenStdin"":false,""WorkingDir"":""/app"","
          + @"""Cmd"":[""sh"",""-c"",""echo hello""],"
          + @"""Env"":[""PATH=/usr/bin""],""Labels"":{""app"":""test""}},"
          + @"""NetworkSettings"":{""Bridge"":"""",""Gateway"":""172.17.0.1"","
          + @"""IPAddress"":""172.17.0.2"",""MacAddress"":""02:42:ac:11:00:02""}}";

      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "abc123fullhash", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      var c = result.Data;
      Assert.Equal("abc123fullhash", c.Id);
      Assert.Equal("my-container", c.Name);
      Assert.Equal("sha256:abc", c.Image);
      Assert.Equal("overlay2", c.Driver);
      // State
      Assert.Equal("running", c.State.Status);
      Assert.True(c.State.Running);
      Assert.False(c.State.Paused);
      Assert.Equal(12345, c.State.Pid);
      Assert.Equal(0, c.State.ExitCode);
      // Config
      Assert.Equal("abc123", c.Config.Hostname);
      Assert.Equal("alpine:latest", c.Config.Image);
      Assert.True(c.Config.Tty);
      Assert.Equal("/app", c.Config.WorkingDir);
      Assert.Equal(new[] { "sh", "-c", "echo hello" }, c.Config.Cmd);
      Assert.Equal(new[] { "PATH=/usr/bin" }, c.Config.Env);
      Assert.Equal("test", c.Config.Labels["app"]);
      // NetworkSettings
      Assert.Equal("172.17.0.1", c.NetworkSettings.Gateway);
      Assert.Equal("172.17.0.2", c.NetworkSettings.IPAddress);
      Assert.Equal("02:42:ac:11:00:02", c.NetworkSettings.MacAddress);
    }

    [Fact]
    public async Task InspectAsync_StripsLeadingSlashFromName()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, @"{""Id"":""x"",""Name"":""/leading-slash""}");

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal("leading-slash", result.Data.Name);
    }

    [Fact]
    public async Task InspectAsync_404_ReturnsNotFoundErrorCode()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 404, @"{""message"":""no such container""}");

      var result = await driver.InspectAsync(Ctx, "nonexistent", cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, result.ErrorCode);
      Assert.Contains("no such container", result.Error);
    }

    // ── ListAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ParsesContainerArray()
    {
      const string json =
          @"[{""Id"":""c1"",""Image"":""alpine"",""Created"":1700000000,"
          + @"""Names"":[""/web-server""],""State"":""running""},"
          + @"{""Id"":""c2"",""Image"":""nginx"",""Created"":1700000100,"
          + @"""Names"":[""/proxy""],""State"":""exited""}]";

      var (driver, mock) = CreateDriver();
      mock.SetupGet("/containers/json", 200, json);

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Count);

      Assert.Equal("c1", result.Data[0].Id);
      Assert.Equal("alpine", result.Data[0].Image);
      Assert.Equal("web-server", result.Data[0].Name);
      Assert.Equal("running", result.Data[0].State.Status);

      Assert.Equal("c2", result.Data[1].Id);
      Assert.Equal("nginx", result.Data[1].Image);
      Assert.Equal("proxy", result.Data[1].Name);
      Assert.Equal("exited", result.Data[1].State.Status);
    }

    [Fact]
    public async Task ListAsync_EmptyArray_ReturnsEmptyList()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/containers/json", 200, "[]");

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    // ── StatsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task StatsAsync_ParsesCpuAndMemoryStats()
    {
      const string json =
          @"{""name"":""/my-container"","
          + @"""cpu_stats"":{""cpu_usage"":{""total_usage"":200000000},"
          + @"""system_cpu_usage"":2000000000,""online_cpus"":4},"
          + @"""precpu_stats"":{""cpu_usage"":{""total_usage"":100000000},"
          + @"""system_cpu_usage"":1000000000},"
          + @"""memory_stats"":{""usage"":52428800,""limit"":1073741824},"
          + @"""pids_stats"":{""current"":5},"
          + @"""networks"":{""eth0"":{""rx_bytes"":1024,""tx_bytes"":2048}},"
          + @"""blkio_stats"":{""io_service_bytes_recursive"":"
          + @"[{""op"":""read"",""value"":4096},{""op"":""write"",""value"":8192}]}}";

      var (driver, mock) = CreateDriver();
      mock.SetupGet("/stats", 200, json);

      var result = await driver.StatsAsync(Ctx, "abc123", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      var s = result.Data;
      Assert.Equal("abc123", s.ContainerId);
      Assert.Equal("my-container", s.Name);
      Assert.Equal(5, s.Pids);
      // CPU: (200M-100M)/(2G-1G) * 4 * 100 = 40%
      Assert.Equal(40.0, s.CpuPercent, 1);
      Assert.Equal(52428800L, s.MemoryUsage);
      Assert.Equal(1073741824L, s.MemoryLimit);
      Assert.True(s.MemoryPercent > 0);
      Assert.Equal(1024L, s.NetworkRxBytes);
      Assert.Equal(2048L, s.NetworkTxBytes);
      Assert.Equal(4096L, s.BlockReadBytes);
      Assert.Equal(8192L, s.BlockWriteBytes);
    }

    [Fact]
    public async Task StatsAsync_WithNoNetworks_DefaultsToZero()
    {
      const string json =
          @"{""name"":""/bare"","
          + @"""cpu_stats"":{""cpu_usage"":{""total_usage"":0},""system_cpu_usage"":0},"
          + @"""precpu_stats"":{""cpu_usage"":{""total_usage"":0},""system_cpu_usage"":0},"
          + @"""memory_stats"":{""usage"":0,""limit"":0},"
          + @"""pids_stats"":{""current"":1}}";

      var (driver, mock) = CreateDriver();
      mock.SetupGet("/stats", 200, json);

      var result = await driver.StatsAsync(Ctx, "bare", cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(result.Success);
      Assert.Equal(0L, result.Data.NetworkRxBytes);
      Assert.Equal(0L, result.Data.NetworkTxBytes);
      Assert.Equal(0L, result.Data.BlockReadBytes);
      Assert.Equal(0L, result.Data.BlockWriteBytes);
      Assert.Equal(0.0, result.Data.CpuPercent);
    }

    [Fact]
    public async Task StatsAsync_404_ReturnsNotFoundErrorCode()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/stats", 404, @"{""message"":""no such container""}");

      var result = await driver.StatsAsync(Ctx, "ghost", cancellationToken: TestContext.Current.CancellationToken);
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, result.ErrorCode);
    }
  }
}
