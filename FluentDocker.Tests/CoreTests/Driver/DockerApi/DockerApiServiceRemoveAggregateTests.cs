using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// A-M1: multi-id service <c>RemoveAsync</c> must attempt every id and aggregate failures
  /// instead of aborting on the first failure — mirroring `docker service rm` (CLI driver) and
  /// <c>DockerApiSystemDriver.PruneAsync</c>'s collect-then-fail pattern. Aborting on the first
  /// 404 would silently leave every id after it (including live, resource-consuming services)
  /// un-removed while the caller's error only names the first id.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class DockerApiServiceRemoveAggregateTests
  {
    private static DriverContext Ctx => new("docker-api-service-remove-aggregate-test");

    private static (DockerApiServiceDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);
      return (driver, mock);
    }

    [Fact]
    public async Task RemoveAsync_FirstIdFails_StillAttemptsSecondId()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/stale-id", 404, @"{""message"":""service stale-id not found""}");
      mock.SetupDelete("/services/live-svc", 200, "");

      var result = await driver.RemoveAsync(
          Ctx, ["stale-id", "live-svc"], cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      var requests = mock.GetRequests();
      // Both DELETEs must be issued — the second id is not skipped after the first 404.
      Assert.Contains(requests, r => r.Method == "DELETE" && r.Path.Contains("/services/stale-id"));
      Assert.Contains(requests, r => r.Method == "DELETE" && r.Path.Contains("/services/live-svc"));
    }

    [Fact]
    public async Task RemoveAsync_FirstIdFails_AggregatedErrorNamesFailedIdOnly()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/stale-id", 404, @"{""message"":""service stale-id not found""}");
      mock.SetupDelete("/services/live-svc", 200, "");

      var result = await driver.RemoveAsync(
          Ctx, ["stale-id", "live-svc"], cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, result.ErrorCode);
      Assert.Contains("stale-id", result.Error);
      Assert.DoesNotContain("live-svc", result.Error);
    }

    [Fact]
    public async Task RemoveAsync_AllIdsFail_NamesAllFailedIdsAndCount()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/a", 404, @"{""message"":""a not found""}");
      mock.SetupDelete("/services/b", 404, @"{""message"":""b not found""}");

      var result = await driver.RemoveAsync(
          Ctx, ["a", "b"], cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("2 of 2", result.Error);
      Assert.Contains("a:", result.Error);
      Assert.Contains("b:", result.Error);
    }

    [Fact]
    public async Task RemoveAsync_AllIdsSucceed_ReturnsOk()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/services/x", 200, "");
      mock.SetupDelete("/services/y", 200, "");

      var result = await driver.RemoveAsync(
          Ctx, ["x", "y"], cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var requests = mock.GetRequests();
      Assert.Contains(requests, r => r.Method == "DELETE" && r.Path.Contains("/services/x"));
      Assert.Contains(requests, r => r.Method == "DELETE" && r.Path.Contains("/services/y"));
    }
  }
}
