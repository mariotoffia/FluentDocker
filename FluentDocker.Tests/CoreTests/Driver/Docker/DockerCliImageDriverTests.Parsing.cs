using System.Collections.Generic;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliImageDriver: ParseSize (via reflection),
  /// image removal output parsing, and image load output parsing.
  /// </summary>
  public partial class DockerCliImageDriverTests
  {
    #region ParseSize Tests (via reflection)

    [Theory]
    [InlineData("1.05GB", 1_050_000_000L)]
    [InlineData("500MB", 500_000_000L)]
    [InlineData("100KB", 100_000L)]
    [InlineData("50B", 50L)]
    [InlineData("2.5TB", 2_500_000_000_000L)]
    [InlineData("9.18MB", 9_180_000L)]
    [InlineData("0.5GB", 500_000_000L)]
    public void ParseSize_ValidSizes_ReturnsCorrectBytes(string input, long expected)
    {
      var result = InvokeParseSize(input);

      Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseSize_EmptyString_ReturnsZero()
    {
      var result = InvokeParseSize("");

      Assert.Equal(0L, result);
    }

    [Fact]
    public void ParseSize_Null_ReturnsZero()
    {
      var result = InvokeParseSize(null);

      Assert.Equal(0L, result);
    }

    [Fact]
    public void ParseSize_NA_ReturnsZero()
    {
      var result = InvokeParseSize("N/A");

      Assert.Equal(0L, result);
    }

    [Fact]
    public void ParseSize_ZeroB_ReturnsZero()
    {
      var result = InvokeParseSize("0B");

      Assert.Equal(0L, result);
    }

    [Fact]
    public void ParseSize_UnrecognizedFormat_ReturnsZero()
    {
      var result = InvokeParseSize("unknown");

      Assert.Equal(0L, result);
    }

    [Theory]
    [InlineData("1gb", 1_000_000_000L)]
    [InlineData("500mb", 500_000_000L)]
    [InlineData("100kb", 100_000L)]
    public void ParseSize_CaseInsensitive_ReturnsCorrectBytes(string input, long expected)
    {
      var result = InvokeParseSize(input);

      Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseSize_WhitespaceAround_HandlesGracefully()
    {
      var result = InvokeParseSize("  500MB  ");

      Assert.Equal(500_000_000L, result);
    }

    #endregion

    #region Image Removal Output Parsing

    [Fact]
    public void RemoveOutputParsing_DeletedAndUntagged_ParsesCorrectly()
    {
      var output =
        "Untagged: nginx:latest\n" +
        "Untagged: nginx@sha256:abc123\n" +
        "Deleted: sha256:aaa111\n" +
        "Deleted: sha256:bbb222\n";

      var removeResult = ParseRemoveOutput(output);

      Assert.Equal(2, removeResult.Deleted.Count);
      Assert.Contains("sha256:aaa111", removeResult.Deleted);
      Assert.Contains("sha256:bbb222", removeResult.Deleted);
      Assert.Equal(2, removeResult.Untagged.Count);
      Assert.Contains("nginx:latest", removeResult.Untagged);
      Assert.Contains("nginx@sha256:abc123", removeResult.Untagged);
    }

    [Fact]
    public void RemoveOutputParsing_OnlyDeleted_ParsesCorrectly()
    {
      var output = "Deleted: sha256:abc123\n";

      var removeResult = ParseRemoveOutput(output);

      Assert.Single(removeResult.Deleted);
      Assert.Equal("sha256:abc123", removeResult.Deleted[0]);
      Assert.Empty(removeResult.Untagged);
    }

    [Fact]
    public void RemoveOutputParsing_OnlyUntagged_ParsesCorrectly()
    {
      var output = "Untagged: myimage:v1\n";

      var removeResult = ParseRemoveOutput(output);

      Assert.Empty(removeResult.Deleted);
      Assert.Single(removeResult.Untagged);
      Assert.Equal("myimage:v1", removeResult.Untagged[0]);
    }

    [Fact]
    public void RemoveOutputParsing_EmptyOutput_ReturnsEmptyLists()
    {
      var removeResult = ParseRemoveOutput("");

      Assert.Empty(removeResult.Deleted);
      Assert.Empty(removeResult.Untagged);
    }

    [Fact]
    public void RemoveOutputParsing_UnrecognizedLines_AreIgnored()
    {
      var output =
        "Some warning message\n" +
        "Deleted: sha256:abc123\n" +
        "Another line\n";

      var removeResult = ParseRemoveOutput(output);

      Assert.Single(removeResult.Deleted);
      Assert.Equal("sha256:abc123", removeResult.Deleted[0]);
      Assert.Empty(removeResult.Untagged);
    }

    #endregion

    #region Image Load Output Parsing

    [Fact]
    public void LoadOutputParsing_LoadedImage_ParsesCorrectly()
    {
      var output = "Loaded image: nginx:latest\n";

      var images = ParseLoadOutput(output);

      Assert.Single(images);
      Assert.Equal("nginx:latest", images[0]);
    }

    [Fact]
    public void LoadOutputParsing_LoadedImageId_ParsesCorrectly()
    {
      var output = "Loaded image ID: sha256:abc123def456\n";

      var images = ParseLoadOutput(output);

      Assert.Single(images);
      Assert.Equal("sha256:abc123def456", images[0]);
    }

    [Fact]
    public void LoadOutputParsing_MultipleImages_ParsesAll()
    {
      var output =
        "Loaded image: nginx:latest\n" +
        "Loaded image: redis:7\n" +
        "Loaded image ID: sha256:xyz789\n";

      var images = ParseLoadOutput(output);

      Assert.Equal(3, images.Count);
      Assert.Equal("nginx:latest", images[0]);
      Assert.Equal("redis:7", images[1]);
      Assert.Equal("sha256:xyz789", images[2]);
    }

    [Fact]
    public void LoadOutputParsing_EmptyOutput_ReturnsEmpty()
    {
      var images = ParseLoadOutput("");

      Assert.Empty(images);
    }

    [Fact]
    public void LoadOutputParsing_NoMatchingLines_ReturnsEmpty()
    {
      var output = "Some irrelevant output\nAnother line\n";

      var images = ParseLoadOutput(output);

      Assert.Empty(images);
    }

    [Fact]
    public void LoadOutputParsing_ImageIdPrefixTakesPrecedenceOverImagePrefix()
    {
      // "Loaded image ID:" starts with "Loaded image:" too, so the code
      // must check the longer prefix first.
      var output = "Loaded image ID: sha256:abc123\n";

      var images = ParseLoadOutput(output);

      Assert.Single(images);
      Assert.Equal("sha256:abc123", images[0]);
    }

    [Fact]
    public void LoadOutputParsing_ImageWithPort_PreservesFullTag()
    {
      // Verifies that colon in "registry:5000/myapp:v1" is preserved.
      var output = "Loaded image: registry:5000/myapp:v1\n";

      var images = ParseLoadOutput(output);

      Assert.Single(images);
      Assert.Equal("registry:5000/myapp:v1", images[0]);
    }

    #endregion

    #region Parsing Helpers

    /// <summary>
    /// Replicates the removal output parsing logic from RemoveAsync
    /// to test it in isolation without executing Docker commands.
    /// </summary>
    private static ImageRemoveResult ParseRemoveOutput(string output)
    {
      var lineSeparators = new[] { '\n', '\r' };
      var removeResult = new ImageRemoveResult();
      var lines = output.Split(lineSeparators, System.StringSplitOptions.RemoveEmptyEntries);

      foreach (var line in lines)
      {
        if (line.StartsWith("Deleted:"))
          removeResult.Deleted.Add(line.Substring(8).Trim());
        else if (line.StartsWith("Untagged:"))
          removeResult.Untagged.Add(line.Substring(9).Trim());
      }

      return removeResult;
    }

    /// <summary>
    /// Replicates the load output parsing logic from LoadAsync
    /// to test it in isolation without executing Docker commands.
    /// </summary>
    private static List<string> ParseLoadOutput(string output)
    {
      var lineSeparators = new[] { '\n', '\r' };
      var images = new List<string>();
      var lines = output.Split(lineSeparators, System.StringSplitOptions.RemoveEmptyEntries);
      const string loadedPrefix = "Loaded image:";
      const string loadedIdPrefix = "Loaded image ID:";

      foreach (var line in lines)
      {
        if (line.StartsWith(loadedIdPrefix, System.StringComparison.OrdinalIgnoreCase))
        {
          images.Add(line.Substring(loadedIdPrefix.Length).Trim());
        }
        else if (line.StartsWith(loadedPrefix, System.StringComparison.OrdinalIgnoreCase))
        {
          images.Add(line.Substring(loadedPrefix.Length).Trim());
        }
      }

      return images;
    }

    #endregion
  }
}
