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
      var result = await driver.CreateAsync(Ctx, config);

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
      var result = await driver.CreateAsync(Ctx, config);

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
      var result = await driver.CreateAsync(Ctx, config);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.CreateFailed, result.ErrorCode);
    }

    // ── RemoveAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/svc1", 200, "");

      var result = await driver.RemoveAsync(Ctx, new[] { "svc1" });

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

      var result = await driver.RemoveAsync(Ctx, new[] { "missing" });

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
      var result = await driver.UpdateAsync(Ctx, "svc-abc", config);

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
      var result = await driver.UpdateAsync(Ctx, "gone", config);

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

      var result = await driver.RollbackAsync(Ctx, "svc-abc");

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

      var result = await driver.RollbackAsync(Ctx, "gone");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
    }

    // ── ListAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsParsedServices()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services", 200, ServiceListJson);

      var result = await driver.ListAsync(Ctx);

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

      var result = await driver.ListAsync(Ctx);

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task ListAsync_ServerError_ReturnsListFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services", 500, @"{""message"":""err""}");

      var result = await driver.ListAsync(Ctx);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.ListFailed, result.ErrorCode);
    }

    // ── InspectAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task InspectAsync_ReturnsParsedDetails()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);

      var result = await driver.InspectAsync(Ctx, "svc-abc");

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

      var result = await driver.InspectAsync(Ctx, "gsvc");

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

      var result = await driver.InspectAsync(Ctx, "missing");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
    }

    // ── GetTasksAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetTasksAsync_ReturnsParsedTasks()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/tasks", 200, TaskListJson);

      var result = await driver.GetTasksAsync(Ctx, "web");

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

      var result = await driver.GetTasksAsync(Ctx, "empty-svc");

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task GetTasksAsync_ServerError_ReturnsTasksFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/tasks", 500, @"{""message"":""err""}");

      var result = await driver.GetTasksAsync(Ctx, "web");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.TasksFailed, result.ErrorCode);
    }

    // ── GetLogsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_ReturnsLogContent()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc1/logs", 200,
          @"""line1\nline2\nline3""");

      var result = await driver.GetLogsAsync(Ctx, "svc1");

      Assert.True(result.Success);
      Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_ServerError_ReturnsLogsFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc1/logs", 500,
          @"{""message"":""logs error""}");

      var result = await driver.GetLogsAsync(Ctx, "svc1");

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.LogsFailed, result.ErrorCode);
    }

    [Fact]
    public async Task GetLogsAsync_WithConfig_IncludesQueryParams()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc1/logs", 200, @"""ok""");

      var config = new ServiceLogsConfig { Tail = 50, Timestamps = true };
      var result = await driver.GetLogsAsync(Ctx, "svc1", config);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      var req = requests.First(r =>
          r.Method == "GET" && r.Path.Contains("/services/svc1/logs"));
      Assert.Contains("tail=50", req.Path);
      Assert.Contains("timestamps=true", req.Path);
    }

    // ── ScaleAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ScaleAsync_UpdatesReplicaCount()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/svc-abc", 200, ServiceJson);
      mock.SetupPost("/services/svc-abc/update", 200, "{}");

      var replicas = new Dictionary<string, int> { ["svc-abc"] = 10 };
      var result = await driver.ScaleAsync(Ctx, replicas);

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
      var result = await driver.ScaleAsync(Ctx, replicas);

      Assert.True(result.Success);
    }

    [Fact]
    public async Task ScaleAsync_InspectFails_ReturnsFail()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/services/gone", 404,
          @"{""message"":""not found""}");

      var replicas = new Dictionary<string, int> { ["gone"] = 5 };
      var result = await driver.ScaleAsync(Ctx, replicas);

      Assert.False(result.Success);
    }
  }
}
