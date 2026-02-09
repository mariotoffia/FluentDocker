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
  public class DockerApiImageDriverTests
  {
    private static DriverContext Ctx => new("docker-api-image-test");

    private static DockerApiImageDriver CreateDriver(MockDockerApiConnection conn)
    {
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(new DriverContext("test"));
      return driver;
    }

    #region ListAsync

    [Fact]
    public async Task ListAsync_ReturnsImages_WithParsedFields()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/json", 200,
          @"[{""Id"":""sha256:abc123"",""RepoTags"":[""nginx:latest"",""nginx:1.25""],"
          + @"""Created"":1700000000,""Size"":187654321,""ParentId"":""sha256:parent1"","
          + @"""VirtualSize"":187654321,""Labels"":{""maintainer"":""test""},""Containers"":3}]");

      var driver = CreateDriver(conn);
      var result = await driver.ListAsync(Ctx);

      Assert.True(result.Success);
      Assert.Single(result.Data);
      var img = result.Data[0];
      Assert.Equal("sha256:abc123", img.Id);
      Assert.Equal(2, img.RepoTags.Count);
      Assert.Contains("nginx:latest", img.RepoTags);
      Assert.Contains("nginx:1.25", img.RepoTags);
      Assert.Equal(187654321L, img.Size);
      Assert.Equal("sha256:parent1", img.ParentId);
      Assert.Equal("test", img.Labels["maintainer"]);
      Assert.Equal(3, img.Containers);
      // Created = Unix 1700000000 = 2023-11-14T22:13:20Z
      Assert.Equal(2023, img.Created.Year);
    }

    [Fact]
    public async Task ListAsync_WithFilter_IncludesQueryParams()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/json", 200, "[]");
      var driver = CreateDriver(conn);
      var filter = new ImageListFilter
      {
        All = true,
        Reference = "nginx",
        Dangling = true,
        Labels = new Dictionary<string, string> { { "env", "prod" } }
      };

      var result = await driver.ListAsync(Ctx, filter);
      Assert.True(result.Success);

      var request = conn.GetRequests()
          .First(r => r.Method == "GET" && r.Path.Contains("/images/json"));
      Assert.Contains("all=true", request.Path);
      Assert.Contains("filters=", request.Path);
      Assert.Contains("reference", request.Path);
      Assert.Contains("dangling", request.Path);
      Assert.Contains("label", request.Path);
    }

    [Fact]
    public async Task ListAsync_EmptyArray_ReturnsEmptyList()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/json", 200, "[]");
      var driver = CreateDriver(conn);

      var result = await driver.ListAsync(Ctx);
      Assert.True(result.Success);
      Assert.Empty(result.Data);
    }

    #endregion

    #region InspectAsync

    [Fact]
    public async Task InspectAsync_ReturnsDetailedImage_WithAllFields()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/", 200,
          @"{""Id"":""sha256:def456"",""Parent"":""sha256:parentdef"","
          + @"""RepoTags"":[""myapp:v2""],""RepoDigests"":[""myapp@sha256:digest1""],"
          + @"""Created"":""2024-01-15T10:30:00Z"",""Size"":95000000,"
          + @"""VirtualSize"":95000000,""Architecture"":""amd64"",""Os"":""linux"","
          + @"""Config"":{""Labels"":{""version"":""2.0"",""app"":""myapp""}}}");

      var driver = CreateDriver(conn);
      var result = await driver.InspectAsync(Ctx, "myapp:v2");
      Assert.True(result.Success);

      var img = result.Data;
      Assert.Equal("sha256:def456", img.Id);
      Assert.Equal("sha256:parentdef", img.ParentId);
      Assert.Single(img.RepoTags);
      Assert.Equal("myapp:v2", img.RepoTags[0]);
      Assert.Single(img.RepoDigests);
      Assert.Equal("myapp@sha256:digest1", img.RepoDigests[0]);
      Assert.Equal(95000000L, img.Size);
      Assert.Equal("amd64", img.Architecture);
      Assert.Equal("linux", img.Os);
      Assert.Equal("2.0", img.Labels["version"]);
      Assert.Equal("myapp", img.Labels["app"]);
    }

    [Fact]
    public async Task InspectAsync_404_ReturnsImageNotFoundErrorCode()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/", 404,
          @"{""message"":""no such image: nonexistent:latest""}");
      var driver = CreateDriver(conn);

      var result = await driver.InspectAsync(Ctx, "nonexistent:latest");
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.NotFound, result.ErrorCode);
      Assert.Contains("no such image", result.Error);
    }

    #endregion

    #region HistoryAsync

    [Fact]
    public async Task HistoryAsync_ReturnsLayers_WithCreatedByAndSize()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/", 200,
          @"[{""Id"":""sha256:layer1"","
          + @"""CreatedBy"":""/bin/sh -c #(nop) CMD [nginx]"","
          + @"""Created"":1700000000,""Size"":0,"
          + @"""Comment"":""buildkit"",""Tags"":[""nginx:latest""]},"
          + @"{""Id"":""sha256:layer2"","
          + @"""CreatedBy"":""/bin/sh -c apt-get update"","
          + @"""Created"":1699999000,""Size"":45000000,"
          + @"""Comment"":"""",""Tags"":[]}]");

      var driver = CreateDriver(conn);
      var result = await driver.HistoryAsync(Ctx, "nginx:latest");
      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Count);

      var layer0 = result.Data[0];
      Assert.Equal("sha256:layer1", layer0.Id);
      Assert.Equal("/bin/sh -c #(nop) CMD [nginx]", layer0.CreatedBy);
      Assert.Equal(0L, layer0.Size);
      Assert.Equal("buildkit", layer0.Comment);
      Assert.Contains("nginx:latest", layer0.Tags);

      var layer1 = result.Data[1];
      Assert.Equal("/bin/sh -c apt-get update", layer1.CreatedBy);
      Assert.Equal(45000000L, layer1.Size);
    }

    #endregion

    #region TagAsync

    [Fact]
    public async Task TagAsync_Returns201_ReportsSuccess()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/images/", 201, "");
      var driver = CreateDriver(conn);

      var result = await driver.TagAsync(Ctx, "nginx:latest", "myrepo/nginx", "v1");
      Assert.True(result.Success);

      var request = conn.GetRequests()
          .First(r => r.Method == "POST" && r.Path.Contains("/tag"));
      Assert.Contains("repo=", request.Path);
      Assert.Contains("tag=v1", request.Path);
    }

    [Fact]
    public async Task TagAsync_FailsOn404_ReturnsTagFailedErrorCode()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/images/", 404, @"{""message"":""no such image""}");
      var driver = CreateDriver(conn);

      var result = await driver.TagAsync(Ctx, "missing:latest", "myrepo/nginx", "v1");
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.TagFailed, result.ErrorCode);
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_ParsesDeletedAndUntaggedLists()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupDelete("/images/", 200,
          @"[{""Deleted"":""sha256:abc123""},{""Untagged"":""nginx:latest""},"
          + @"{""Deleted"":""sha256:def456""},{""Untagged"":""nginx:1.25""}]");
      var driver = CreateDriver(conn);

      var result = await driver.RemoveAsync(Ctx, "nginx:latest");
      Assert.True(result.Success);
      Assert.Equal(2, result.Data.Deleted.Count);
      Assert.Contains("sha256:abc123", result.Data.Deleted);
      Assert.Contains("sha256:def456", result.Data.Deleted);
      Assert.Equal(2, result.Data.Untagged.Count);
      Assert.Contains("nginx:latest", result.Data.Untagged);
      Assert.Contains("nginx:1.25", result.Data.Untagged);
    }

    [Fact]
    public async Task RemoveAsync_FailsOn404_ReturnsRemoveFailedErrorCode()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupDelete("/images/", 404, @"{""message"":""no such image""}");
      var driver = CreateDriver(conn);

      var result = await driver.RemoveAsync(Ctx, "missing:latest");
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.RemoveFailed, result.ErrorCode);
    }

    #endregion

    #region PruneAsync

    [Fact]
    public async Task PruneAsync_ParsesSpaceReclaimedAndDeletedImages()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/images/prune", 200,
          @"{""SpaceReclaimed"":1024,""ImagesDeleted"":"
          + @"[{""Deleted"":""sha256:abc""},{""Untagged"":""old:latest""}]}");
      var driver = CreateDriver(conn);

      var result = await driver.PruneAsync(Ctx);
      Assert.True(result.Success);
      Assert.Equal(1024L, result.Data.SpaceReclaimed);
      Assert.Equal(2, result.Data.ImagesDeleted.Count);
      Assert.Contains("sha256:abc", result.Data.ImagesDeleted);
      Assert.Contains("old:latest", result.Data.ImagesDeleted);
    }

    [Fact]
    public async Task PruneAsync_WithAllFlag_IncludesDanglingFilter()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/images/prune", 200,
          @"{""SpaceReclaimed"":0,""ImagesDeleted"":[]}");
      var driver = CreateDriver(conn);

      var result = await driver.PruneAsync(Ctx, all: true);
      Assert.True(result.Success);

      var request = conn.GetRequests()
          .First(r => r.Method == "POST" && r.Path.Contains("/images/prune"));
      Assert.Contains("filters=", request.Path);
      Assert.Contains("dangling", request.Path);
    }

    [Fact]
    public async Task PruneAsync_FailsOnServerError()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/images/prune", 500, @"{""message"":""prune failed""}");
      var driver = CreateDriver(conn);

      var result = await driver.PruneAsync(Ctx);
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PruneFailed, result.ErrorCode);
    }

    #endregion

    #region PullAsync

    [Fact]
    public async Task PullAsync_StreamsNdjsonProgress_ReturnsSuccess()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create",
          "{\"status\":\"Pulling from library/nginx\",\"id\":\"latest\"}\n"
          + "{\"status\":\"Downloading\",\"progressDetail\":{\"current\":500,\"total\":1000},\"id\":\"abc123\"}\n"
          + "{\"status\":\"Pull complete\",\"id\":\"abc123\"}\n");
      var driver = CreateDriver(conn);
      var progress = new List<ImagePullProgress>();
      var reporter = new Progress<ImagePullProgress>(p => progress.Add(p));

      var result = await driver.PullAsync(Ctx, "nginx", "latest", reporter, default);
      Assert.True(result.Success);
    }

    [Fact]
    public async Task PullAsync_WithError_ReturnsFail()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create",
          "{\"error\":\"repository not found\",\"errorDetail\":{\"message\":\"repository not found\"}}\n");
      var driver = CreateDriver(conn);

      var result = await driver.PullAsync(Ctx, "nonexistent/image", "latest", null, default);
      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PullFailed, result.ErrorCode);
      Assert.Contains("repository not found", result.Error);
    }

    #endregion

    #region Multiple Images in List

    [Fact]
    public async Task ListAsync_MultipleImages_ReturnsAll()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/images/json", 200,
          @"[{""Id"":""sha256:img1"",""RepoTags"":[""alpine:3.18""],""Created"":1700000000,""Size"":7000000},"
          + @"{""Id"":""sha256:img2"",""RepoTags"":[""ubuntu:22.04""],""Created"":1700001000,""Size"":77000000},"
          + @"{""Id"":""sha256:img3"",""RepoTags"":[""redis:7""],""Created"":1700002000,""Size"":130000000}]");
      var driver = CreateDriver(conn);

      var result = await driver.ListAsync(Ctx);
      Assert.True(result.Success);
      Assert.Equal(3, result.Data.Count);
      Assert.Equal("sha256:img1", result.Data[0].Id);
      Assert.Equal("sha256:img2", result.Data[1].Id);
      Assert.Equal("sha256:img3", result.Data[2].Id);
    }

    #endregion
  }
}
