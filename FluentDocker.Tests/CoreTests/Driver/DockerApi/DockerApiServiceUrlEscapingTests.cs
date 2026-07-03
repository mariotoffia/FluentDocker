using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiServiceUrlEscapingTests
  {
    private static DriverContext Ctx => new("docker-api-service-url-test");

    [Fact]
    public async Task InspectAsync_EscapesServiceIdPathSegment()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/services/", 200,
          @"{""ID"":""svc/a?b#c"",""Version"":{""Index"":1},""Spec"":{""Name"":""svc""}}");
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.InspectAsync(
          Ctx, "svc/a?b#c", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "GET");
      Assert.Contains("/services/svc%2Fa%3Fb%23c", request.Path);
    }

    [Fact]
    public async Task GetLogsAsync_EscapesServiceIdAndSinceQueryValue()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/logs", "log");
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.GetLogsAsync(Ctx, "svc/a?b#c",
          new ServiceLogsConfig { Since = "2024-01-01T00:00:00+02:00?x" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "GET_STREAM");
      Assert.Contains("/services/svc%2Fa%3Fb%23c/logs", request.Path);
      Assert.Contains("since=2024-01-01T00%3A00%3A00%2B02%3A00%3Fx", request.Path);
    }
  }
}
