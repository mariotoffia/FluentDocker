using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IImageDriver operations.
  /// Requires Docker daemon to be running.
  /// </summary>
  [Trait("Category", "Integration")]
  [Collection("DockerDriver")]
  public partial class ImageDriverTests : DockerDriverTestBase
  {
    [Fact]
    public async Task Pull_ValidImage_PullsSuccessfully()
    {
      // Act
      var result = await ImageDriver.PullAsync(Context, "alpine", "latest");

      // Assert
      Assert.True(result.Success, $"Pull failed: {result.Error}");
    }

    [Fact]
    public async Task List_AfterPull_ContainsImage()
    {
      // Arrange
      await ImageDriver.PullAsync(Context, "alpine", "latest");

      // Act
      var result = await ImageDriver.ListAsync(Context);

      // Assert
      Assert.True(result.Success);
      Assert.Contains(result.Data, img =>
          img.RepoTags != null && img.RepoTags.Any(t => t.Contains("alpine")));
    }

    [Fact]
    public async Task Inspect_ExistingImage_ReturnsDetails()
    {
      // Arrange
      await ImageDriver.PullAsync(Context, "alpine", "latest");

      // Act
      var result = await ImageDriver.InspectAsync(Context, "alpine:latest");

      // Assert
      Assert.True(result.Success, $"Inspect failed: {result.Error}");
      Assert.NotNull(result.Data);
      Assert.NotNull(result.Data.Id);
    }

    [Fact]
    public async Task History_ExistingImage_ReturnsLayers()
    {
      // Arrange
      await ImageDriver.PullAsync(Context, "alpine", "latest");

      // Act
      var result = await ImageDriver.HistoryAsync(Context, "alpine:latest");

      // Assert
      Assert.True(result.Success, $"History failed: {result.Error}");
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Count > 0, "Should have at least one layer");
    }

    [Fact]
    public async Task Tag_ExistingImage_TagsSuccessfully()
    {
      // Arrange
      await ImageDriver.PullAsync(Context, "alpine", "latest");
      var newRepo = "test-tag-repo";
      var newTag = "test-tag";

      try
      {
        // Act
        var result = await ImageDriver.TagAsync(Context, "alpine:latest", newRepo, newTag);

        // Assert
        Assert.True(result.Success, $"Tag failed: {result.Error}");

        // Verify tag exists
        var images = await ImageDriver.ListAsync(Context, new ImageListFilter
        {
          Reference = $"{newRepo}:{newTag}"
        });
        Assert.True(images.Success);
        Assert.True(images.Data.Count > 0);
      }
      finally
      {
        // Cleanup
        await ImageDriver.RemoveAsync(Context, $"{newRepo}:{newTag}");
      }
    }

    [Fact]
    public async Task List_WithFilter_FiltersResults()
    {
      // Arrange
      await ImageDriver.PullAsync(Context, "alpine", "latest");

      // Act
      var result = await ImageDriver.ListAsync(Context, new ImageListFilter
      {
        Reference = "alpine:latest"
      });

      // Assert
      Assert.True(result.Success);
      Assert.True(result.Data.Count >= 1);
    }

    [Fact]
    public async Task ImageConfiguration_IsRetrievable()
    {
      // Arrange
      await ImageDriver.PullAsync(Context, "postgres", "13-alpine");

      // Act
      var result = await ImageDriver.InspectAsync(Context, "postgres:13-alpine");

      // Assert
      Assert.True(result.Success);
      Assert.NotNull(result.Data);
      Assert.NotNull(result.Data.Architecture);
      Assert.NotNull(result.Data.Os);
    }

    [Fact]
    public async Task ImageIsExposed_OnRunningContainer()
    {
      string containerId = null;
      try
      {
        // Arrange
        await ImageDriver.PullAsync(Context, "postgres", "13-alpine");

        // Run a container
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = "postgres:13-alpine",
          Environment = new System.Collections.Generic.Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          Detach = true
        });
        containerId = runResult.Data.Id;

        // Act
        var containerInspect = await ContainerDriver.InspectAsync(Context, containerId);
        var imageInspect = await ImageDriver.InspectAsync(Context, "postgres:13-alpine");

        // Assert
        Assert.True(containerInspect.Success);
        Assert.True(imageInspect.Success);
        Assert.Equal(containerInspect.Data.Image, imageInspect.Data.Id);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Remove_ExistingImage_RemovesSuccessfully()
    {
      // Arrange - Pull and tag an image
      await ImageDriver.PullAsync(Context, "alpine", "latest");
      var testRepo = "test-remove-image";
      var testTag = "to-remove";
      await ImageDriver.TagAsync(Context, "alpine:latest", testRepo, testTag);

      // Act
      var result = await ImageDriver.RemoveAsync(Context, $"{testRepo}:{testTag}");

      // Assert
      Assert.True(result.Success, $"Remove failed: {result.Error}");

      // Verify image is gone
      var images = await ImageDriver.ListAsync(Context, new ImageListFilter
      {
        Reference = $"{testRepo}:{testTag}"
      });
      Assert.True(images.Data.Count == 0);
    }
  }
}

