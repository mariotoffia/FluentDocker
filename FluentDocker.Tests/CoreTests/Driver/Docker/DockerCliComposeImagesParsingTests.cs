using System;
using System.Collections.Generic;
using FluentDocker.Drivers;
using Newtonsoft.Json;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for docker compose images NDJSON parsing.
  /// Validates that the ComposeImage model correctly deserializes
  /// docker compose images --format json output, including the
  /// ID-to-ImageId property mapping.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliComposeImagesParsingTests
  {
    #region Single Line Parsing

    [Fact]
    public void ParseSingleImage_AllFields_DeserializesCorrectly()
    {
      var json = @"{""Container"":""myapp-web-1"",""Repository"":""nginx"",""Tag"":""latest"",""ID"":""sha256:abc123"",""Size"":""187MB""}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Equal("myapp-web-1", image.Container);
      Assert.Equal("nginx", image.Repository);
      Assert.Equal("latest", image.Tag);
      Assert.Equal("sha256:abc123", image.ImageId);
      Assert.Equal("187MB", image.Size);
    }

    [Fact]
    public void ParseSingleImage_IdMapsToImageId_ViaJsonProperty()
    {
      var json = @"{""ID"":""sha256:deadbeef""}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Equal("sha256:deadbeef", image.ImageId);
    }

    [Fact]
    public void ParseSingleImage_MissingFields_DefaultsToNull()
    {
      var json = @"{""Container"":""web-1""}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Equal("web-1", image.Container);
      Assert.Null(image.Repository);
      Assert.Null(image.Tag);
      Assert.Null(image.ImageId);
      Assert.Null(image.Size);
    }

    [Fact]
    public void ParseSingleImage_EmptyObject_AllNulls()
    {
      var json = @"{}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Null(image.Container);
      Assert.Null(image.Repository);
      Assert.Null(image.Tag);
      Assert.Null(image.ImageId);
      Assert.Null(image.Size);
    }

    #endregion

    #region NDJSON Multi-Line Parsing

    [Fact]
    public void ParseNdjson_MultipleLines_ParsesAllImages()
    {
      var ndjson =
          @"{""Container"":""myapp-web-1"",""Repository"":""nginx"",""Tag"":""1.25"",""ID"":""sha256:aaa"",""Size"":""187MB""}" + "\n" +
          @"{""Container"":""myapp-db-1"",""Repository"":""postgres"",""Tag"":""16"",""ID"":""sha256:bbb"",""Size"":""412MB""}" + "\n" +
          @"{""Container"":""myapp-redis-1"",""Repository"":""redis"",""Tag"":""7-alpine"",""ID"":""sha256:ccc"",""Size"":""30MB""}";

      var images = ParseNdjsonImages(ndjson);

      Assert.Equal(3, images.Count);

      Assert.Equal("myapp-web-1", images[0].Container);
      Assert.Equal("nginx", images[0].Repository);
      Assert.Equal("1.25", images[0].Tag);
      Assert.Equal("sha256:aaa", images[0].ImageId);
      Assert.Equal("187MB", images[0].Size);

      Assert.Equal("myapp-db-1", images[1].Container);
      Assert.Equal("postgres", images[1].Repository);
      Assert.Equal("16", images[1].Tag);
      Assert.Equal("sha256:bbb", images[1].ImageId);
      Assert.Equal("412MB", images[1].Size);

      Assert.Equal("myapp-redis-1", images[2].Container);
      Assert.Equal("redis", images[2].Repository);
      Assert.Equal("7-alpine", images[2].Tag);
      Assert.Equal("sha256:ccc", images[2].ImageId);
      Assert.Equal("30MB", images[2].Size);
    }

    [Fact]
    public void ParseNdjson_WithCarriageReturns_ParsesCorrectly()
    {
      var ndjson =
          @"{""Container"":""web-1"",""Repository"":""nginx"",""Tag"":""latest"",""ID"":""sha256:111"",""Size"":""100MB""}" + "\r\n" +
          @"{""Container"":""api-1"",""Repository"":""node"",""Tag"":""20"",""ID"":""sha256:222"",""Size"":""900MB""}";

      var images = ParseNdjsonImages(ndjson);

      Assert.Equal(2, images.Count);
      Assert.Equal("web-1", images[0].Container);
      Assert.Equal("api-1", images[1].Container);
    }

    [Fact]
    public void ParseNdjson_EmptyOutput_ReturnsEmptyList()
    {
      var images = ParseNdjsonImages("");

      Assert.NotNull(images);
      Assert.Empty(images);
    }

    [Fact]
    public void ParseNdjson_WhitespaceOnly_ReturnsEmptyList()
    {
      var images = ParseNdjsonImages("  \n  \r\n  ");

      Assert.NotNull(images);
      Assert.Empty(images);
    }

    [Fact]
    public void ParseNdjson_InvalidLineSkipped_ValidLinesStillParsed()
    {
      var ndjson =
          @"{""Container"":""web-1"",""Repository"":""nginx"",""Tag"":""latest"",""ID"":""sha256:aaa"",""Size"":""100MB""}" + "\n" +
          "this is not valid json" + "\n" +
          @"{""Container"":""db-1"",""Repository"":""postgres"",""Tag"":""16"",""ID"":""sha256:bbb"",""Size"":""400MB""}";

      var images = ParseNdjsonImages(ndjson);

      Assert.Equal(2, images.Count);
      Assert.Equal("web-1", images[0].Container);
      Assert.Equal("db-1", images[1].Container);
    }

    [Fact]
    public void ParseNdjson_SingleLine_ParsesOneImage()
    {
      var ndjson = @"{""Container"":""app-1"",""Repository"":""myapp"",""Tag"":""v2.0"",""ID"":""sha256:xyz"",""Size"":""55MB""}";

      var images = ParseNdjsonImages(ndjson);

      Assert.Single(images);
      Assert.Equal("app-1", images[0].Container);
      Assert.Equal("myapp", images[0].Repository);
      Assert.Equal("v2.0", images[0].Tag);
      Assert.Equal("sha256:xyz", images[0].ImageId);
      Assert.Equal("55MB", images[0].Size);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ParseImage_ExtraFieldsIgnored_StillDeserializes()
    {
      var json = @"{""Container"":""web-1"",""Repository"":""nginx"",""Tag"":""latest"",""ID"":""sha256:abc"",""Size"":""100MB"",""ExtraField"":""ignored""}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Equal("web-1", image.Container);
      Assert.Equal("sha256:abc", image.ImageId);
    }

    [Fact]
    public void ParseImage_NullValues_HandledGracefully()
    {
      var json = @"{""Container"":null,""Repository"":null,""Tag"":null,""ID"":null,""Size"":null}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Null(image.Container);
      Assert.Null(image.Repository);
      Assert.Null(image.Tag);
      Assert.Null(image.ImageId);
      Assert.Null(image.Size);
    }

    [Fact]
    public void ParseImage_LongImageId_PreservedFully()
    {
      var longId = "sha256:e4720093a3c13555d7e73629483e2027c41de13tried5098fe0f0cbfb743a7b0";
      var json = $@"{{""ID"":""{longId}""}}";

      var image = JsonConvert.DeserializeObject<ComposeImage>(json);

      Assert.NotNull(image);
      Assert.Equal(longId, image.ImageId);
    }

    [Fact]
    public void ParseImage_RealisticDockerOutput_ParsesCorrectly()
    {
      // Simulates real docker compose images --format json output
      var ndjson =
          @"{""Container"":""project-frontend-1"",""Repository"":""node"",""Tag"":""20-alpine"",""ID"":""sha256:7d3a4b58e1c2"",""Size"":""180.3MB""}" + "\n" +
          @"{""Container"":""project-backend-1"",""Repository"":""project-backend"",""Tag"":""latest"",""ID"":""sha256:a1b2c3d4e5f6"",""Size"":""350.7MB""}" + "\n" +
          @"{""Container"":""project-db-1"",""Repository"":""postgres"",""Tag"":""16.2-alpine"",""ID"":""sha256:f6e5d4c3b2a1"",""Size"":""241.1MB""}" + "\n" +
          @"{""Container"":""project-cache-1"",""Repository"":""redis"",""Tag"":""7.2-alpine"",""ID"":""sha256:1a2b3c4d5e6f"",""Size"":""30.2MB""}";

      var images = ParseNdjsonImages(ndjson);

      Assert.Equal(4, images.Count);

      // Verify frontend
      Assert.Equal("project-frontend-1", images[0].Container);
      Assert.Equal("node", images[0].Repository);
      Assert.Equal("20-alpine", images[0].Tag);
      Assert.Equal("sha256:7d3a4b58e1c2", images[0].ImageId);
      Assert.Equal("180.3MB", images[0].Size);

      // Verify backend (custom built image)
      Assert.Equal("project-backend-1", images[1].Container);
      Assert.Equal("project-backend", images[1].Repository);
      Assert.Equal("latest", images[1].Tag);

      // Verify database
      Assert.Equal("project-db-1", images[2].Container);
      Assert.Equal("postgres", images[2].Repository);
      Assert.Equal("16.2-alpine", images[2].Tag);

      // Verify cache
      Assert.Equal("project-cache-1", images[3].Container);
      Assert.Equal("redis", images[3].Repository);
      Assert.Equal("7.2-alpine", images[3].Tag);
      Assert.Equal("30.2MB", images[3].Size);
    }

    #endregion

    #region Helper

    /// <summary>
    /// Replicates the NDJSON parsing logic used in
    /// <see cref="FluentDocker.Drivers.Docker.Cli.Components.DockerCliComposeDriver"/>
    /// ImagesAsync method, so we can test it without async infrastructure.
    /// </summary>
    private static List<ComposeImage> ParseNdjsonImages(string output)
    {
      var images = new List<ComposeImage>();
      var lines = output.Split(
          new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines)
      {
        try
        {
          var image = JsonConvert.DeserializeObject<ComposeImage>(line);
          if (image != null)
            images.Add(image);
        }
        catch { }
      }
      return images;
    }

    #endregion
  }
}
