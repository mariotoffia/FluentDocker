using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
  /// <summary>
  /// Docker API driver integration tests for image operations:
  /// Tag, Remove, History, Prune, Inspect.
  /// </summary>
  public partial class DockerApiDriverTests
  {
    #region Image Inspect Tests

    [Fact]
    public async Task Image_Inspect_ReturnsDetails()
    {
      await EnsureImageAsync(TestImage);

      var listResult = await ImageDriver.ListAsync(Context);
      Assert.True(listResult.Success);

      var alpine = listResult.Data.FirstOrDefault(
          i => i.RepoTags?.Any(t => t.Contains("alpine")) == true) ?? throw new Exception("$XunitDynamicSkip$Alpine image not found");

      var inspectResult = await ImageDriver.InspectAsync(Context, alpine.Id);

      Assert.True(inspectResult.Success,
          $"Inspect failed: {inspectResult.Error}");
      Assert.NotNull(inspectResult.Data);
    }

    #endregion

    #region Image Tag Tests

    [Fact]
    public async Task Image_Tag_CreatesNewTag()
    {
      await EnsureImageAsync(TestImage);
      var tagName = $"test-tag-{Guid.NewGuid():N}"[..20];

      try
      {
        var listResult = await ImageDriver.ListAsync(Context);
        Assert.True(listResult.Success);

        var alpine = listResult.Data.FirstOrDefault(
            i => i.RepoTags?.Any(t => t.Contains("alpine")) == true) ?? throw new Exception("$XunitDynamicSkip$Alpine image not found");

        var tagResult = await ImageDriver.TagAsync(
            Context, alpine.Id, "localhost/test-repo", tagName);

        Assert.True(tagResult.Success,
            $"Tag failed: {tagResult.Error}");

        // Verify new tag exists
        var verifyList = await ImageDriver.ListAsync(Context);
        Assert.Contains(verifyList.Data,
            i => i.RepoTags?.Any(
                t => t.Contains(tagName)) == true);
      }
      finally
      {
        // Clean up the tagged image
        try
        {
          await ImageDriver.RemoveAsync(
              Context, $"localhost/test-repo:{tagName}");
        }
        catch { }
      }
    }

    #endregion

    #region Image History Tests

    [Fact]
    public async Task Image_History_ReturnsLayers()
    {
      await EnsureImageAsync(TestImage);

      var listResult = await ImageDriver.ListAsync(Context);
      var alpine = listResult.Data.FirstOrDefault(
          i => i.RepoTags?.Any(t => t.Contains("alpine")) == true) ?? throw new Exception("$XunitDynamicSkip$Alpine image not found");

      var historyResult = await ImageDriver.HistoryAsync(
          Context, alpine.Id);

      Assert.True(historyResult.Success,
          $"History failed: {historyResult.Error}");
      Assert.NotNull(historyResult.Data);
      Assert.True(historyResult.Data.Count > 0,
          "Should have at least one layer");
    }

    #endregion

    #region Image Remove Tests

    [Fact]
    public async Task Image_Remove_RemovesImage()
    {
      // Pull a specific image to remove
      await EnsureImageAsync(BusyboxImage);

      // Tag it so we can safely remove the tag without losing the base image
      var listResult = await ImageDriver.ListAsync(Context);
      var busybox = listResult.Data.FirstOrDefault(
          i => i.RepoTags?.Any(t => t.Contains("busybox")) == true) ?? throw new Exception("$XunitDynamicSkip$Busybox image not found");

      var tempTag = $"remove-test-{Guid.NewGuid():N}"[..20];
      await ImageDriver.TagAsync(
          Context, busybox.Id, "localhost/remove-test", tempTag);

      var removeResult = await ImageDriver.RemoveAsync(
          Context, $"localhost/remove-test:{tempTag}");

      Assert.True(removeResult.Success,
          $"Remove failed: {removeResult.Error}");
    }

    #endregion

    #region Image Prune Tests

    [Fact]
    public async Task Image_Prune_Succeeds()
    {
      var pruneResult = await ImageDriver.PruneAsync(Context);

      Assert.True(pruneResult.Success,
          $"Prune failed: {pruneResult.Error}");
    }

    #endregion

    #region Image Error Tests

    [Fact]
    public async Task Image_Inspect_NonExistent_Fails()
    {
      var fakeId = "sha256:" + new string('a', 64);
      var result = await ImageDriver.InspectAsync(Context, fakeId);
      Assert.False(result.Success);
    }

    [Fact]
    public async Task Image_Remove_NonExistent_Fails()
    {
      var fakeImage = $"nonexistent-{Guid.NewGuid():N}"[..20] + ":latest";
      var result = await ImageDriver.RemoveAsync(Context, fakeImage);
      Assert.False(result.Success);
    }

    #endregion
  }
}
