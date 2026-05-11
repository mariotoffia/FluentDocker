using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  // Inspection-parsing tolerance for podman-api-compat responses: Entrypoint
  // and Cmd may arrive as a single string, and Health.Status may be an empty
  // string instead of an omitted field. See community PR #303 (Rory Reid).
  [Trait("Category", "Unit")]
  public class DockerApiContainerDriverPodmanCompatTests
  {
    private static DriverContext Ctx => new("docker-api-test");

    private static (DockerApiContainerDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(new DriverContext("docker-api-test"));
      return (driver, mock);
    }

    [Fact]
    public async Task InspectAsync_EntrypointAsArray_ParsesAsArray()
    {
      const string json = @"{""Id"":""x"",""Config"":{""Entrypoint"":[""docker-entrypoint.sh""]}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(new[] { "docker-entrypoint.sh" }, result.Data.Config.EntryPoint);
    }

    [Fact]
    public async Task InspectAsync_EntrypointAsSingleString_ParsesAsSingleElementArray()
    {
      // Podman sometimes emits Entrypoint as a bare string instead of an array.
      const string json = @"{""Id"":""x"",""Config"":{""Entrypoint"":""docker-entrypoint.sh""}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(new[] { "docker-entrypoint.sh" }, result.Data.Config.EntryPoint);
    }

    [Fact]
    public async Task InspectAsync_CmdAsSingleString_ParsesAsSingleElementArray()
    {
      const string json = @"{""Id"":""x"",""Config"":{""Cmd"":""postgres""}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(new[] { "postgres" }, result.Data.Config.Cmd);
    }

    [Fact]
    public async Task InspectAsync_HealthBlockHealthyStatus_ParsesToHealthy()
    {
      const string json = @"{""Id"":""x"",""State"":{""Status"":""running"","
          + @"""Health"":{""Status"":""healthy"",""FailingStreak"":0,""Log"":[]}}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.NotNull(result.Data.State.Health);
      Assert.Equal(HealthState.Healthy, result.Data.State.Health.Status);
      Assert.Equal(0, result.Data.State.Health.FailingStreak);
    }

    [Fact]
    public async Task InspectAsync_HealthBlockEmptyStatus_ParsesToUnknown()
    {
      // Podman emits Health.Status as an empty string in some states (e.g. "created").
      const string json = @"{""Id"":""x"",""State"":{""Status"":""created"","
          + @"""Health"":{""Status"":"""",""FailingStreak"":0,""Log"":null}}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.NotNull(result.Data.State.Health);
      Assert.Equal(HealthState.Unknown, result.Data.State.Health.Status);
    }

    [Fact]
    public async Task InspectAsync_HealthBlockMissing_LeavesHealthNull()
    {
      const string json = @"{""Id"":""x"",""State"":{""Status"":""running""}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Null(result.Data.State.Health);
    }

    [Fact]
    public async Task InspectAsync_HealthBlockWithLog_ParsesLogEntries()
    {
      const string json = @"{""Id"":""x"",""State"":{""Status"":""running"","
          + @"""Health"":{""Status"":""unhealthy"",""FailingStreak"":3,"
          + @"""Log"":[{""Start"":""2026-05-11T10:00:00Z"",""End"":""2026-05-11T10:00:01Z"","
          + @"""ExitCode"":1,""Output"":""boom""}]}}}";
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/json", 200, json);

      var result = await driver.InspectAsync(Ctx, "x", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var health = result.Data.State.Health;
      Assert.NotNull(health);
      Assert.Equal(HealthState.Unhealthy, health.Status);
      Assert.Equal(3, health.FailingStreak);
      Assert.NotNull(health.Log);
      Assert.Single(health.Log);
      Assert.Equal(1, health.Log[0].ExitCode);
      Assert.Equal("boom", health.Log[0].Output);
    }
  }
}
