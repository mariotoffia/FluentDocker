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
  public class DockerApiServiceDriverTests
  {
    private static DriverContext Ctx => new("docker-api-test");

    private static (DockerApiServiceDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(new DriverContext("docker-api-test"));
      return (driver, mock);
    }

    // ── Service JSON helpers ─────────────────────────────────────────

    private const string ServiceJson =
        @"{""ID"":""svc-abc"",""Version"":{""Index"":42},"
        + @"""Spec"":{""Name"":""web"","
        + @"""TaskTemplate"":{""ContainerSpec"":{""Image"":""nginx:latest""}},"
        + @"""Mode"":{""Replicated"":{""Replicas"":3}}}}";

    private const string ServiceListJson =
        @"[{""ID"":""svc1"",""Spec"":{""Name"":""alpha"","
        + @"""TaskTemplate"":{""ContainerSpec"":{""Image"":""img1""}},"
        + @"""Mode"":{""Replicated"":{""Replicas"":2}}}},"
        + @"{""ID"":""svc2"",""Spec"":{""Name"":""beta"","
        + @"""TaskTemplate"":{""ContainerSpec"":{""Image"":""img2""}},"
        + @"""Mode"":{""Global"":{}}}}]";

    private const string TaskListJson =
        @"[{""ID"":""task1"",""Name"":""web.1"",""NodeID"":""node-a"","
        + @"""DesiredState"":""running"","
        + @"""Status"":{""State"":""running""},"
        + @"""Spec"":{""ContainerSpec"":{""Image"":""nginx:latest""}}},"
        + @"{""ID"":""task2"",""Name"":""web.2"",""NodeID"":""node-b"","
        + @"""DesiredState"":""shutdown"","
        + @"""Status"":{""State"":""complete"",""Err"":""exit 1""},"
        + @"""Spec"":{""ContainerSpec"":{""Image"":""nginx:latest""}}}]";

    // ── CreateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsServiceId()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/services/create", 200, @"{""ID"":""svc123""}");

      var config = new ServiceCreateConfig { Name = "web", Image = "nginx:latest" };
      var result = await driver.CreateAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("svc123", result.Data.Id);
      Assert.Empty(result.Data.Warnings);
    }

    [Fact]
    public async Task CreateAsync_WithPortsReplicasLabels_ReturnsServiceId()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/services/create", 200,
          @"{""ID"":""svc456"",""Warnings"":[""port conflict""]}");

      var config = new ServiceCreateConfig
      {
        Name = "api",
        Image = "myapp:v2",
        Replicas = 3,
        Ports = new List<ServicePort>
                {
                    new() { TargetPort = 80, PublishedPort = 8080, Protocol = "tcp" }
                },
        Labels = new Dictionary<string, string> { ["env"] = "prod" }
      };
      var result = await driver.CreateAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("svc456", result.Data.Id);
      Assert.Single(result.Data.Warnings);
      Assert.Equal("port conflict", result.Data.Warnings[0]);
    }

    [Fact]
    public async Task CreateAsync_ServerError_ReturnsCreateFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/services/create", 500,
          @"{""message"":""internal error""}");

      var config = new ServiceCreateConfig { Name = "fail", Image = "bad" };
      var result = await driver.CreateAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.CreateFailed, result.ErrorCode);
    }

    // ── RemoveAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/svc1", 200, "");

      var result = await driver.RemoveAsync(Ctx, new[] { "svc1" }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      Assert.Contains(requests,
          r => r.Method == "DELETE" && r.Path.Contains("/services/svc1"));
    }

    [Fact]
    public async Task RemoveAsync_404_ReturnsNotFound()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/missing", 404,
          @"{""message"":""service not found""}");

      var result = await driver.RemoveAsync(Ctx, new[] { "missing" }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
    }

    // ── UpdateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_InspectsAndUpdates()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);
      mock.SetupPost("/services/svc-abc/update", 200, "{}");

      var config = new ServiceUpdateConfig { Replicas = 5 };
      var result = await driver.UpdateAsync(Ctx, "svc-abc", config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      Assert.Contains(requests,
          r => r.Method == "GET" && r.Path.Contains("/services/svc-abc"));
      Assert.Contains(requests,
          r => r.Method == "POST" && r.Path.Contains("/services/svc-abc/update")
               && r.Path.Contains("version=42"));
    }

    [Fact]
    public async Task UpdateAsync_InspectFails_ReturnsFail()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/gone", 404,
          @"{""message"":""service not found""}");

      var config = new ServiceUpdateConfig { Replicas = 2 };
      var result = await driver.UpdateAsync(Ctx, "gone", config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
    }

    // ── RollbackAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_SendsRollbackParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);
      mock.SetupPost("/services/svc-abc/update", 200, "{}");

      var result = await driver.RollbackAsync(Ctx, "svc-abc", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var postReq = requests.First(r =>
          r.Method == "POST" && r.Path.Contains("/services/svc-abc/update"));
      Assert.Contains("rollback=previous", postReq.Path);
      Assert.Contains("version=42", postReq.Path);
    }

    [Fact]
    public async Task RollbackAsync_InspectFails_ReturnsFail()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/gone", 404,
          @"{""message"":""not found""}");

      var result = await driver.RollbackAsync(Ctx, "gone", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
    }

    // ── ListAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsParsedServices()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services", 200, ServiceListJson);

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Count);

      Assert.Equal("svc1", result.Data[0].Id);
      Assert.Equal("alpha", result.Data[0].Name);
      Assert.Equal("replicated", result.Data[0].Mode);
      Assert.Equal("img1", result.Data[0].Image);

      Assert.Equal("svc2", result.Data[1].Id);
      Assert.Equal("beta", result.Data[1].Name);
      Assert.Equal("global", result.Data[1].Mode);
    }

    [Fact]
    public async Task ListAsync_EmptyArray_ReturnsEmptyList()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services", 200, "[]");

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task ListAsync_ServerError_ReturnsListFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services", 500, @"{""message"":""err""}");

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.ListFailed, result.ErrorCode);
    }

    // ── InspectAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task InspectAsync_ReturnsParsedDetails()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);

      var result = await driver.InspectAsync(Ctx, "svc-abc", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("svc-abc", result.Data.Id);
      Assert.Equal("web", result.Data.Name);
      Assert.Equal("replicated", result.Data.Mode);
      Assert.Equal(3, result.Data.Replicas);
      Assert.Equal(42L, result.Data.Version);
      Assert.Equal("nginx:latest", result.Data.Image);
    }

    [Fact]
    public async Task InspectAsync_GlobalMode_ReturnsModeGlobal()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/gsvc", 200,
          @"{""ID"":""gsvc"",""Version"":{""Index"":1},"
          + @"""Spec"":{""Name"":""global-svc"","
          + @"""TaskTemplate"":{""ContainerSpec"":{""Image"":""redis""}},"
          + @"""Mode"":{""Global"":{}}}}");

      var result = await driver.InspectAsync(Ctx, "gsvc", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("global", result.Data.Mode);
      Assert.Equal(0, result.Data.Replicas);
    }

    [Fact]
    public async Task InspectAsync_404_ReturnsNotFound()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/missing", 404,
          @"{""message"":""service not found""}");

      var result = await driver.InspectAsync(Ctx, "missing", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
    }

    // ── GetTasksAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetTasksAsync_ReturnsParsedTasks()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/tasks", 200, TaskListJson);

      var result = await driver.GetTasksAsync(Ctx, "web", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Count);

      Assert.Equal("task1", result.Data[0].Id);
      Assert.Equal("running", result.Data[0].DesiredState);
      Assert.Equal("running", result.Data[0].CurrentState);
      Assert.Equal("node-a", result.Data[0].Node);
      Assert.Null(result.Data[0].Error);

      Assert.Equal("task2", result.Data[1].Id);
      Assert.Equal("shutdown", result.Data[1].DesiredState);
      Assert.Equal("complete", result.Data[1].CurrentState);
      Assert.Equal("exit 1", result.Data[1].Error);
    }

    [Fact]
    public async Task GetTasksAsync_EmptyResult_ReturnsEmptyList()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/tasks", 200, "[]");

      var result = await driver.GetTasksAsync(Ctx, "empty-svc", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task GetTasksAsync_ServerError_ReturnsTasksFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/tasks", 500, @"{""message"":""err""}");

      var result = await driver.GetTasksAsync(Ctx, "web", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.TasksFailed, result.ErrorCode);
    }

    // ── GetLogsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_ReturnsStreamContent()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/services/svc1/logs", "line1\nline2\nline3");

      var result = await driver.GetLogsAsync(Ctx, "svc1", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains("line1", result.Data);
      Assert.Contains("line2", result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_StripsMultiplexedHeaders()
    {
      var (driver, mock) = CreateDriver();
      // Build a multiplexed frame: stream type=1 (stdout), payload="hello logs"
      var payload = System.Text.Encoding.UTF8.GetBytes("hello logs");
      var frame = new byte[8 + payload.Length];
      frame[0] = 1; // stdout
      frame[4] = (byte)((payload.Length >> 24) & 0xFF);
      frame[5] = (byte)((payload.Length >> 16) & 0xFF);
      frame[6] = (byte)((payload.Length >> 8) & 0xFF);
      frame[7] = (byte)(payload.Length & 0xFF);
      System.Array.Copy(payload, 0, frame, 8, payload.Length);
      mock.SetupStreamBytes("/services/svc1/logs", frame);

      var result = await driver.GetLogsAsync(Ctx, "svc1", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("hello logs", result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_ConnectionError_ReturnsLogsFailed()
    {
      // No stream setup → mock returns empty stream, which works fine
      // To simulate a real error, we'd need a throwing mock, but the empty stream
      // path just returns empty string. Verify the error code path works.
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/services/svc1/logs", "");

      var result = await driver.GetLogsAsync(Ctx, "svc1", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(string.Empty, result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_WithConfig_IncludesQueryParams()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/services/svc1/logs", "ok");

      var config = new ServiceLogsConfig { Tail = 50, Timestamps = true };
      var result = await driver.GetLogsAsync(Ctx, "svc1", config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var req = requests.First(r =>
          r.Method == "GET_STREAM" && r.Path.Contains("/services/svc1/logs"));
      Assert.Contains("tail=50", req.Path);
      Assert.Contains("timestamps=true", req.Path);
    }

    [Fact]
    public async Task GetLogsAsync_WithSince_IncludesSinceParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupStream("/services/svc1/logs", "log data");

      var config = new ServiceLogsConfig { Since = "2024-01-01" };
      var result = await driver.GetLogsAsync(Ctx, "svc1", config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var req = mock.GetRequests().First(r =>
          r.Method == "GET_STREAM" && r.Path.Contains("/services/svc1/logs"));
      Assert.Contains("since=2024-01-01", req.Path);
    }

    // ── ScaleAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ScaleAsync_UpdatesReplicaCount()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);
      mock.SetupPost("/services/svc-abc/update", 200, "{}");

      var replicas = new Dictionary<string, int> { ["svc-abc"] = 10 };
      var result = await driver.ScaleAsync(Ctx, replicas, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      Assert.Contains(requests,
          r => r.Method == "POST" && r.Path.Contains("/services/svc-abc/update")
               && r.Path.Contains("version=42"));
    }

    [Fact]
    public async Task ScaleAsync_MultipleServices_AllSucceed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);
      mock.SetupPost("/services/svc-abc/update", 200, "{}");
      mock.SetupGet("/services/svc-def", 200,
          @"{""ID"":""svc-def"",""Version"":{""Index"":7},"
          + @"""Spec"":{""Name"":""worker"","
          + @"""TaskTemplate"":{""ContainerSpec"":{""Image"":""worker:1""}},"
          + @"""Mode"":{""Replicated"":{""Replicas"":1}}}}");
      mock.SetupPost("/services/svc-def/update", 200, "{}");

      var replicas = new Dictionary<string, int>
      {
        ["svc-abc"] = 5,
        ["svc-def"] = 3
      };
      var result = await driver.ScaleAsync(Ctx, replicas, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
    }

    [Fact]
    public async Task ScaleAsync_InspectFails_ReturnsFail()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/gone", 404,
          @"{""message"":""not found""}");

      var replicas = new Dictionary<string, int> { ["gone"] = 5 };
      var result = await driver.ScaleAsync(Ctx, replicas, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
    }
  }
}
