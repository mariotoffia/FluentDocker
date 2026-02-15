using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliSystemDriver disk usage parsing.
  /// Validates ParseDiskUsageOutput, ParseHumanReadableBytes, and
  /// ParseReclaimableBytes against Docker CLI JSON output formats.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliSystemDriverDiskUsageTests
  {
    #region ParseDiskUsageOutput Tests

    [Fact]
    public void ParseDiskUsageOutput_AllFourTypes_ParsesCorrectly()
    {
      var output = string.Join("\n",
          "{\"Type\":\"Images\",\"TotalCount\":5,\"Active\":3,\"Size\":\"1.234GB\",\"Reclaimable\":\"500MB (40%)\"}",
          "{\"Type\":\"Containers\",\"TotalCount\":3,\"Active\":1,\"Size\":\"100MB\",\"Reclaimable\":\"50MB (50%)\"}",
          "{\"Type\":\"Local Volumes\",\"TotalCount\":2,\"Active\":1,\"Size\":\"200MB\",\"Reclaimable\":\"100MB (50%)\"}",
          "{\"Type\":\"Build Cache\",\"TotalCount\":10,\"Active\":5,\"Size\":\"300MB\",\"Reclaimable\":\"150MB (50%)\"}"
      );

      var info = DockerCliSystemDriver.ParseDiskUsageOutput(output);

      // Images
      Assert.Equal(5, info.Images.TotalCount);
      Assert.Equal(3, info.Images.Active);
      Assert.Equal(1_234_000_000L, info.Images.Size);
      Assert.Equal(500_000_000L, info.Images.Reclaimable);

      // Containers
      Assert.Equal(3, info.Containers.TotalCount);
      Assert.Equal(1, info.Containers.Active);
      Assert.Equal(100_000_000L, info.Containers.Size);
      Assert.Equal(50_000_000L, info.Containers.Reclaimable);

      // Volumes
      Assert.Equal(2, info.Volumes.TotalCount);
      Assert.Equal(1, info.Volumes.Active);
      Assert.Equal(200_000_000L, info.Volumes.Size);
      Assert.Equal(100_000_000L, info.Volumes.Reclaimable);

      // Build Cache
      Assert.Equal(10, info.BuildCache.TotalCount);
      Assert.Equal(5, info.BuildCache.Active);
      Assert.Equal(300_000_000L, info.BuildCache.Size);
      Assert.Equal(150_000_000L, info.BuildCache.Reclaimable);
    }

    [Fact]
    public void ParseDiskUsageOutput_TotalSizeAndReclaimable_AreSumsOfItems()
    {
      var output = string.Join("\n",
          "{\"Type\":\"Images\",\"TotalCount\":1,\"Active\":0,\"Size\":\"1GB\",\"Reclaimable\":\"500MB (50%)\"}",
          "{\"Type\":\"Containers\",\"TotalCount\":1,\"Active\":0,\"Size\":\"2GB\",\"Reclaimable\":\"1GB (50%)\"}",
          "{\"Type\":\"Local Volumes\",\"TotalCount\":1,\"Active\":0,\"Size\":\"3GB\",\"Reclaimable\":\"1.5GB (50%)\"}",
          "{\"Type\":\"Build Cache\",\"TotalCount\":1,\"Active\":0,\"Size\":\"4GB\",\"Reclaimable\":\"2GB (50%)\"}"
      );

      var info = DockerCliSystemDriver.ParseDiskUsageOutput(output);

      var expectedTotalSize = 1_000_000_000L + 2_000_000_000L
                              + 3_000_000_000L + 4_000_000_000L;
      var expectedReclaimable = 500_000_000L + 1_000_000_000L
                               + 1_500_000_000L + 2_000_000_000L;

      Assert.Equal(expectedTotalSize, info.TotalSize);
      Assert.Equal(expectedReclaimable, info.Reclaimable);
    }

    [Fact]
    public void ParseDiskUsageOutput_OnlyImages_OtherItemsAreDefaults()
    {
      var output = "{\"Type\":\"Images\",\"TotalCount\":2,\"Active\":1,\"Size\":\"50MB\",\"Reclaimable\":\"25MB (50%)\"}";

      var info = DockerCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(2, info.Images.TotalCount);
      Assert.Equal(0, info.Containers.TotalCount);
      Assert.Equal(0, info.Volumes.TotalCount);
      Assert.Equal(0, info.BuildCache.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_EmptyString_ReturnsEmptyInfo()
    {
      var info = DockerCliSystemDriver.ParseDiskUsageOutput("");

      Assert.NotNull(info);
      Assert.Equal(0, info.TotalSize);
      Assert.Equal(0, info.Reclaimable);
      Assert.Equal(0, info.Images.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_NullInput_ReturnsEmptyInfo()
    {
      var info = DockerCliSystemDriver.ParseDiskUsageOutput(null);

      Assert.NotNull(info);
      Assert.Equal(0, info.TotalSize);
    }

    [Fact]
    public void ParseDiskUsageOutput_MalformedJson_SkipsLine()
    {
      var output = string.Join("\n",
          "{\"Type\":\"Images\",\"TotalCount\":5,\"Active\":3,\"Size\":\"1GB\",\"Reclaimable\":\"500MB (50%)\"}",
          "this is not json",
          "{\"Type\":\"Containers\",\"TotalCount\":2,\"Active\":1,\"Size\":\"100MB\",\"Reclaimable\":\"50MB (50%)\"}"
      );

      var info = DockerCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(5, info.Images.TotalCount);
      Assert.Equal(2, info.Containers.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_WindowsLineEndings_ParsesCorrectly()
    {
      var output = "{\"Type\":\"Images\",\"TotalCount\":3,\"Active\":1,\"Size\":\"2GB\",\"Reclaimable\":\"1GB (50%)\"}\r\n" +
                   "{\"Type\":\"Containers\",\"TotalCount\":1,\"Active\":0,\"Size\":\"50MB\",\"Reclaimable\":\"50MB (100%)\"}";

      var info = DockerCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(3, info.Images.TotalCount);
      Assert.Equal(1, info.Containers.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_ZeroSizes_ParsesCorrectly()
    {
      var output = "{\"Type\":\"Images\",\"TotalCount\":0,\"Active\":0,\"Size\":\"0B\",\"Reclaimable\":\"0B\"}";

      var info = DockerCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(0, info.Images.TotalCount);
      Assert.Equal(0, info.Images.Size);
      Assert.Equal(0, info.Images.Reclaimable);
    }

    #endregion

    #region ParseHumanReadableBytes Tests

    [Theory]
    [InlineData("0B", 0)]
    [InlineData("100B", 100)]
    [InlineData("1.5B", 1)]
    public void ParseHumanReadableBytes_ByteSuffix_CorrectValue(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("1kB", 1000)]
    [InlineData("1.5kB", 1500)]
    [InlineData("500kB", 500_000)]
    public void ParseHumanReadableBytes_KiloByteSuffix_Base1000(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("1KB", 1000)]
    [InlineData("2.5KB", 2500)]
    public void ParseHumanReadableBytes_UppercaseKB_Base1000(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("1MB", 1_000_000)]
    [InlineData("100MB", 100_000_000)]
    [InlineData("1.5MB", 1_500_000)]
    public void ParseHumanReadableBytes_MegaByteSuffix_Base1000(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("1GB", 1_000_000_000)]
    [InlineData("1.234GB", 1_234_000_000)]
    [InlineData("2.5GB", 2_500_000_000)]
    public void ParseHumanReadableBytes_GigaByteSuffix_Base1000(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("1TB", 1_000_000_000_000)]
    [InlineData("2.5TB", 2_500_000_000_000)]
    public void ParseHumanReadableBytes_TeraByteSuffix_Base1000(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("1KiB", 1024)]
    [InlineData("1MiB", 1_048_576)]
    [InlineData("1GiB", 1_073_741_824)]
    [InlineData("1TiB", 1_099_511_627_776)]
    public void ParseHumanReadableBytes_BinaryPrefixes_Base1024(string input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("   ", 0)]
    public void ParseHumanReadableBytes_EmptyOrNull_ReturnsZero(string? input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseHumanReadableBytes(input));
    }

    [Fact]
    public void ParseHumanReadableBytes_RawNumber_ParsesAsBytes()
    {
      Assert.Equal(12345, DockerCliSystemDriver.ParseHumanReadableBytes("12345"));
    }

    [Fact]
    public void ParseHumanReadableBytes_InvalidString_ReturnsZero()
    {
      Assert.Equal(0, DockerCliSystemDriver.ParseHumanReadableBytes("notanumber"));
    }

    #endregion

    #region ParseReclaimableBytes Tests

    [Fact]
    public void ParseReclaimableBytes_WithPercentage_StripsAndParses()
    {
      Assert.Equal(500_000_000L,
          DockerCliSystemDriver.ParseReclaimableBytes("500MB (40%)"));
    }

    [Fact]
    public void ParseReclaimableBytes_WithoutPercentage_ParsesDirectly()
    {
      Assert.Equal(500_000_000L,
          DockerCliSystemDriver.ParseReclaimableBytes("500MB"));
    }

    [Fact]
    public void ParseReclaimableBytes_ZeroWithPercentage_ReturnsZero()
    {
      Assert.Equal(0L,
          DockerCliSystemDriver.ParseReclaimableBytes("0B (0%)"));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseReclaimableBytes_EmptyOrNull_ReturnsZero(string? input, long expected)
    {
      Assert.Equal(expected, DockerCliSystemDriver.ParseReclaimableBytes(input));
    }

    [Fact]
    public void ParseReclaimableBytes_PercentageWithSpaces_Parses()
    {
      Assert.Equal(1_000_000_000L,
          DockerCliSystemDriver.ParseReclaimableBytes("1GB  (100%)"));
    }

    #endregion
  }
}
