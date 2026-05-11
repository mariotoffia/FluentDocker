using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliImageDriver JSON parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliImageDriverTests
  {
    #region ParseImageList Tests

    [Fact]
    public void ParseImageList_JsonArray_ReturnsImages()
    {
      var json = @"[
                {""Id"":""sha256:abc123"",""Names"":[""nginx:latest"",""nginx:1.25""],""Size"":50000000},
                {""Id"":""sha256:def456"",""Names"":[""redis:7""],""Size"":30000000}
            ]";

      var result = InvokeParseImageList(json);
      Assert.Equal(2, result.Count);
      Assert.Equal("sha256:abc123", result[0].Id);
      Assert.Contains("nginx:latest", result[0].RepoTags);
      Assert.Contains("nginx:1.25", result[0].RepoTags);
      Assert.Equal(50000000, result[0].Size);
    }

    [Fact]
    public void ParseImageList_AlternateKeys_HandlesRepoTags()
    {
      var json = @"[{""ID"":""sha256:abc"",""RepoTags"":[""alpine:3.18""],""Size"":5000000}]";

      var result = InvokeParseImageList(json);
      Assert.Single(result);
      Assert.Equal("sha256:abc", result[0].Id);
      Assert.Contains("alpine:3.18", result[0].RepoTags);
    }

    [Fact]
    public void ParseImageList_NewlineDelimited_ReturnsImages()
    {
      var json = "{\"Id\":\"sha256:abc\",\"Names\":[\"nginx\"],\"Size\":100}\n"
               + "{\"Id\":\"sha256:def\",\"Names\":[\"redis\"],\"Size\":200}";

      var result = InvokeParseImageList(json);
      Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseImageList_EmptyString_ReturnsEmpty()
    {
      var result = InvokeParseImageList("");
      Assert.Empty(result);
    }

    [Fact]
    public void ParseImageList_NullString_ReturnsEmpty()
    {
      var result = InvokeParseImageList(null);
      Assert.Empty(result);
    }

    #endregion

    #region ParseImageInspect Tests

    [Fact]
    public void ParseImageInspect_ValidArrayJson_ReturnsImage()
    {
      var json = @"[{
                ""Id"": ""sha256:abc123"",
                ""Architecture"": ""amd64"",
                ""Os"": ""linux"",
                ""Size"": 50000000,
                ""VirtualSize"": 60000000,
                ""RepoTags"": [""nginx:latest""]
            }]";

      var result = InvokeParseImageInspect(json);
      Assert.Equal("sha256:abc123", result.Id);
      Assert.Equal("amd64", result.Architecture);
      Assert.Equal("linux", result.Os);
      Assert.Equal(50000000, result.Size);
      Assert.Contains("nginx:latest", result.RepoTags);
    }

    [Fact]
    public void ParseImageInspect_SingleObject_ReturnsImage()
    {
      var json = @"{""Id"":""sha256:abc"",""Architecture"":""arm64"",""Os"":""linux""}";

      var result = InvokeParseImageInspect(json);
      Assert.Equal("sha256:abc", result.Id);
      Assert.Equal("arm64", result.Architecture);
    }

    [Fact]
    public void ParseImageInspect_InvalidJson_ReturnsEmptyImage()
    {
      var result = InvokeParseImageInspect("not json");
      Assert.NotNull(result);
    }

    #endregion

    #region ParseHistory Tests

    [Fact]
    public void ParseHistory_ValidJson_ReturnsLayers()
    {
      var json = @"[
                {""id"":""layer1"",""createdBy"":""ADD file"",""size"":1000,""comment"":""base""},
                {""id"":""layer2"",""createdBy"":""RUN apt-get"",""size"":5000}
            ]";

      var result = InvokeParseHistory(json);
      Assert.Equal(2, result.Count);
      Assert.Equal("layer1", result[0].Id);
      Assert.Equal("ADD file", result[0].CreatedBy);
      Assert.Equal(1000, result[0].Size);
      Assert.Equal("base", result[0].Comment);
      Assert.Equal("layer2", result[1].Id);
    }

    [Fact]
    public void ParseHistory_AlternateKeys_Handles_CamelCase()
    {
      var json = @"[{""Id"":""l1"",""CreatedBy"":""CMD"",""Size"":100,""Comment"":""test""}]";

      var result = InvokeParseHistory(json);
      Assert.Single(result);
      Assert.Equal("l1", result[0].Id);
    }

    [Fact]
    public void ParseHistory_EmptyString_ReturnsEmpty()
    {
      var result = InvokeParseHistory("");
      Assert.Empty(result);
    }

    [Fact]
    public void ParseHistory_NonArrayJson_ReturnsEmpty()
    {
      var result = InvokeParseHistory("{\"key\":\"value\"}");
      Assert.Empty(result);
    }

    #endregion

    #region Reflection Helpers

    private static IList<Image> InvokeParseImageList(string json)
    {
      var method = typeof(PodmanCliImageDriver).GetMethod(
          "ParseImageList",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (IList<Image>)method.Invoke(null, [json]);
    }

    private static Image InvokeParseImageInspect(string json)
    {
      var method = typeof(PodmanCliImageDriver).GetMethod(
          "ParseImageInspect",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (Image)method.Invoke(null, [json]);
    }

    private static IList<ImageLayer> InvokeParseHistory(string json)
    {
      var method = typeof(PodmanCliImageDriver).GetMethod(
          "ParseHistory",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (IList<ImageLayer>)method.Invoke(null, [json]);
    }

    #endregion
  }
}
