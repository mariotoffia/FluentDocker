using System;
using System.Reflection;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// A-H1: PullAsync must split a tag embedded in the repository segment of `image` (e.g.
  /// "nginx:1.25") into fromImage/tag instead of sending the whole reference as fromImage plus a
  /// separate "latest"/explicit tag — the daemon's reference.WithTag silently discards the
  /// embedded tag and pulls the wrong image. Partial of <see cref="DockerApiImageStreamingTests"/>.
  /// </summary>
  public partial class DockerApiImageStreamingTests
  {
    #region A-H1 - Embedded-tag pull reference handling

    [Fact]
    public async Task PullAsync_EmbeddedTagNoExplicitTag_SplitsIntoFromImageAndTag()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");
      // Non-terminal status line triggers the existence-probe fallback (GET /images/{ref}/json).
      conn.SetupGet("/images/", 200, "{}");

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(
          Ctx, "nginx:1.25", null!, null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains("fromImage=nginx&", req.Path);
      Assert.Contains("tag=1.25", req.Path);
      Assert.DoesNotContain("tag=latest", req.Path);
      Assert.DoesNotContain("nginx%3A1.25", req.Path);
    }

    [Fact]
    public async Task PullAsync_BareRepoNoTag_UsesLatest()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      await driver.PullAsync(Ctx, "nginx", null!, null!, TestContext.Current.CancellationToken);

      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains("fromImage=nginx&", req.Path);
      Assert.Contains("tag=latest", req.Path);
    }

    [Fact]
    public async Task PullAsync_RegistryHostColon_IsNotMistakenForEmbeddedTag()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      await driver.PullAsync(
          Ctx, "registry:5000/nginx", null!, null!, TestContext.Current.CancellationToken);

      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains($"fromImage={Uri.EscapeDataString("registry:5000/nginx")}", req.Path);
      Assert.Contains("tag=latest", req.Path);
    }

    [Fact]
    public async Task PullAsync_ExplicitTagMatchesEmbeddedTag_Succeeds()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");
      conn.SetupGet("/images/", 200, "{}");

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(
          Ctx, "nginx:1.25", "1.25", null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains("fromImage=nginx&", req.Path);
      Assert.Contains("tag=1.25", req.Path);
    }

    [Fact]
    public async Task PullAsync_ExplicitTagConflictsWithEmbeddedTag_ReturnsInvalidArgument()
    {
      var conn = new MockDockerApiConnection();

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(
          Ctx, "nginx:1.25", "1.26", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
      Assert.Contains("Conflicting tags", result.Error);
      // The conflict is rejected before any request reaches the daemon.
      Assert.Empty(conn.GetRequests());
    }

    [Fact]
    public async Task PullAsync_DigestReferenceWithColonLikeSegment_IsNotSplit()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      await driver.PullAsync(
          Ctx, "nginx@sha256:abc123", null!, null!, TestContext.Current.CancellationToken);

      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains($"fromImage={Uri.EscapeDataString("nginx@sha256:abc123")}", req.Path);
      Assert.DoesNotContain("&tag=", req.Path);
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("nginx:1.25")]
    [InlineData("registry:5000/nginx")]
    [InlineData("registry.example.com/team/app:2.0")]
    [InlineData("myrepo/myimage")]
    [InlineData("a/b/c:tag")]
    public void EmbeddedTagDetection_MatchesCliShouldAppendTag(string image)
    {
      // Parity check: the API driver's embedded-tag split must agree with the CLI driver's
      // ShouldAppendTag classifier for every non-digest input reachable by PullAsync, or the two
      // drivers would pull different images for the same reference. ShouldAppendTag is a private pure classifier;
      // exercising it through the CLI's public PullAsync would need a live docker binary, so it
      // is reflected into directly per the documented internal-parser test exception.
      var shouldAppendTag = (bool)typeof(DockerCliImageDriver)
          .GetMethod("ShouldAppendTag", BindingFlags.NonPublic | BindingFlags.Static)!
          .Invoke(null, [image, "dummy"])!;
      var hasEmbeddedTag = (bool)typeof(DockerApiImageDriver)
          .GetMethod("TrySplitEmbeddedTag", BindingFlags.NonPublic | BindingFlags.Static)!
          .Invoke(null, [image, null, null])!;

      Assert.Equal(!shouldAppendTag, hasEmbeddedTag);
    }

    #endregion
  }
}
