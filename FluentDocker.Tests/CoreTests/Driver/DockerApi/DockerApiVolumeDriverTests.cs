using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiVolumeDriverTests
  {
    private static DriverContext Ctx => new("docker-api-test");

    private static (DockerApiVolumeDriver driver, MockDockerApiConnection mock) CreateDriver()
    {
      var mock = new MockDockerApiConnection();
      var driver = new DockerApiVolumeDriver(mock);
      driver.Initialize(new DriverContext("docker-api-test"));
      return (driver, mock);
    }

    [Fact]
    public async Task CreateAsync_ReturnsParsedVolume()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/volumes/create", 200,
          @"{""Name"":""my-vol"",""Driver"":""local""}");

      var config = new VolumeCreateConfig { Name = "my-vol" };
      var result = await driver.CreateAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("my-vol", result.Data.Name);
      Assert.Equal("local", result.Data.Driver);
    }

    [Fact]
    public async Task CreateAsync_FallsBackToConfigValues()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/volumes/create", 200, "{}");

      var config = new VolumeCreateConfig { Name = "fallback", Driver = "nfs" };
      var result = await driver.CreateAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("fallback", result.Data.Name);
      Assert.Equal("nfs", result.Data.Driver);
    }

    [Fact]
    public async Task CreateAsync_ServerError_ReturnsCreateFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/volumes/create", 500,
          @"{""message"":""create failed""}");

      var config = new VolumeCreateConfig { Name = "fail-vol" };
      var result = await driver.CreateAsync(Ctx, config, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Volume.CreateFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_ReturnsParsedVolumes()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/volumes", 200,
          @"{""Volumes"":[{""Name"":""v1"",""Driver"":""local"",""Scope"":""local""},"
          + @"{""Name"":""v2"",""Driver"":""nfs"",""Scope"":""global""}]}");

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Count);
      Assert.Equal("v1", result.Data[0].Name);
      Assert.Equal("local", result.Data[0].Driver);
      Assert.Equal("local", result.Data[0].Scope);
      Assert.Equal("v2", result.Data[1].Name);
      Assert.Equal("nfs", result.Data[1].Driver);
      Assert.Equal("global", result.Data[1].Scope);
    }

    [Fact]
    public async Task ListAsync_EmptyVolumes_ReturnsEmptyList()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/volumes", 200, @"{""Volumes"":[]}");

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task ListAsync_MissingVolumesKey_ReturnsEmptyList()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/volumes", 200, "{}");

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    [Fact]
    public async Task InspectAsync_ReturnsParsedVolume()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/volumes/data-vol", 200,
          @"{""Name"":""data-vol"",""Driver"":""local"","
          + @"""Scope"":""local"",""CreatedAt"":""2024-01-15T10:30:00Z""}");

      var result = await driver.InspectAsync(Ctx, "data-vol", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("data-vol", result.Data.Name);
      Assert.Equal("local", result.Data.Driver);
      Assert.Equal("local", result.Data.Scope);
      Assert.Equal(2024, result.Data.Created.Year);
    }

    [Fact]
    public async Task InspectAsync_404_ReturnsNotFoundError()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupGet("/volumes/missing", 404,
          @"{""message"":""volume missing not found""}");

      var result = await driver.InspectAsync(Ctx, "missing", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Volume.NotFound, result.ErrorCode);
      Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task RemoveAsync_ReturnsSuccess()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/volumes/old-vol", 204, "");

      var result = await driver.RemoveAsync(Ctx, "old-vol", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);

      var requests = mock.GetRequests();
      Assert.Contains(requests,
          r => r.Method == "DELETE" && r.Path.Contains("/volumes/old-vol"));
    }

    [Fact]
    public async Task RemoveAsync_WithForce_IncludesForceQueryParam()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/volumes/stuck-vol", 204, "");

      var result = await driver.RemoveAsync(Ctx, "stuck-vol", force: true, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);

      var requests = mock.GetRequests();
      var del = requests.First(r =>
          r.Method == "DELETE" && r.Path.Contains("/volumes/stuck-vol"));
      Assert.Contains("force=true", del.Path);
    }

    [Fact]
    public async Task RemoveAsync_DefaultForce_IsFalse()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupDelete("/volumes/normal-vol", 204, "");

      var result = await driver.RemoveAsync(Ctx, "normal-vol", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);

      var requests = mock.GetRequests();
      var del = requests.First(r =>
          r.Method == "DELETE" && r.Path.Contains("/volumes/normal-vol"));
      Assert.Contains("force=false", del.Path);
    }

    [Fact]
    public async Task PruneAsync_ReturnsSpaceReclaimed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/volumes/prune", 200,
          @"{""VolumesDeleted"":[""old1"",""old2""],""SpaceReclaimed"":2097152}");

      var result = await driver.PruneAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(2097152L, result.Data.SpaceReclaimed);
      Assert.Equal(2, result.Data.VolumesDeleted.Count);
      Assert.Contains("old1", result.Data.VolumesDeleted);
      Assert.Contains("old2", result.Data.VolumesDeleted);
    }

    [Fact]
    public async Task PruneAsync_EmptyResult_ReturnsZeroSpace()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/volumes/prune", 200, "{}");

      var result = await driver.PruneAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(0L, result.Data.SpaceReclaimed);
      Assert.Empty(result.Data.VolumesDeleted);
    }

    [Fact]
    public async Task PruneAsync_ServerError_ReturnsPruneFailed()
    {
      var (driver, mock) = CreateDriver();
      mock.SetupPost("/volumes/prune", 500,
          @"{""message"":""prune error""}");

      var result = await driver.PruneAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Volume.PruneFailed, result.ErrorCode);
    }
  }
}
