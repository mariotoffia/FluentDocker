using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="CliOutputParser.ParseMemoryUsage"/> and
  /// <see cref="CliOutputParser.ParseIOPair"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class CliOutputParserTests
  {
    #region ParseMemoryUsage Tests

    [Fact]
    public void ParseMemoryUsage_ValidMiBAndGiB_ReturnsBothValuesInBytes()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("100MiB / 2GiB");

      // Assert
      Assert.Equal(100L * 1024 * 1024, usage);       // 104857600
      Assert.Equal(2L * 1024 * 1024 * 1024, limit);  // 2147483648
    }

    [Fact]
    public void ParseMemoryUsage_ValidKiBAndMiB_ReturnsBothValuesInBytes()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("512KiB / 256MiB");

      // Assert
      Assert.Equal(512L * 1024, usage);         // 524288
      Assert.Equal(256L * 1024 * 1024, limit);  // 268435456
    }

    [Fact]
    public void ParseMemoryUsage_SiSuffixes_UsesBase1000()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("500MB / 4GB");

      // Assert
      Assert.Equal(500L * 1000 * 1000, usage);         // 500000000
      Assert.Equal(4L * 1000 * 1000 * 1000, limit);    // 4000000000
    }

    [Fact]
    public void ParseMemoryUsage_FractionalValues_ParsesCorrectly()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("1.5GiB / 3.75GiB");

      // Assert
      Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), usage);
      Assert.Equal((long)(3.75 * 1024 * 1024 * 1024), limit);
    }

    [Fact]
    public void ParseMemoryUsage_Null_ReturnsZeros()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage(null);

      // Assert
      Assert.Equal(0, usage);
      Assert.Equal(0, limit);
    }

    [Fact]
    public void ParseMemoryUsage_EmptyString_ReturnsZeros()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("");

      // Assert
      Assert.Equal(0, usage);
      Assert.Equal(0, limit);
    }

    [Fact]
    public void ParseMemoryUsage_WhitespaceOnly_ReturnsZeros()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("   \t  ");

      // Assert
      Assert.Equal(0, usage);
      Assert.Equal(0, limit);
    }

    [Fact]
    public void ParseMemoryUsage_SingleValue_ReturnsUsageOnlyAndZeroLimit()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("256MiB");

      // Assert
      Assert.Equal(256L * 1024 * 1024, usage);
      Assert.Equal(0, limit);
    }

    [Fact]
    public void ParseMemoryUsage_ExtraWhitespace_TrimsAndParses()
    {
      // Arrange & Act
      // The input "  100MiB  /  2GiB  " contains " / " (the substring "  /  " includes " / "),
      // so Split finds it. Each part is then Trim()-ed before ParseByteValue.
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("  100MiB  /  2GiB  ");

      // Assert
      Assert.Equal(100L * 1024 * 1024, usage);
      Assert.Equal(2L * 1024 * 1024 * 1024, limit);
    }

    [Fact]
    public void ParseMemoryUsage_CorrectSlashSeparator_ParsesSuccessfully()
    {
      // Arrange - exact " / " separator (single space each side)
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("100MiB / 2GiB");

      // Assert
      Assert.Equal(100L * 1024 * 1024, usage);
      Assert.Equal(2L * 1024 * 1024 * 1024, limit);
    }

    [Fact]
    public void ParseMemoryUsage_TiBValues_ParsesLargeValues()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("1TiB / 2TiB");

      // Assert
      Assert.Equal((long)(1.0 * 1024 * 1024 * 1024 * 1024), usage);
      Assert.Equal((long)(2.0 * 1024 * 1024 * 1024 * 1024), limit);
    }

    [Fact]
    public void ParseMemoryUsage_RawByteValues_ParsesWithoutSuffix()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("1024 / 2048");

      // Assert
      Assert.Equal(1024, usage);
      Assert.Equal(2048, limit);
    }

    [Theory]
    [InlineData("50B / 100B", 50L, 100L)]
    [InlineData("10kB / 20kB", 10000L, 20000L)]
    [InlineData("10KB / 20KB", 10000L, 20000L)]
    public void ParseMemoryUsage_VariousSuffixes_ParsesCorrectly(
      string input, long expectedUsage, long expectedLimit)
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage(input);

      // Assert
      Assert.Equal(expectedUsage, usage);
      Assert.Equal(expectedLimit, limit);
    }

    [Fact]
    public void ParseMemoryUsage_InvalidText_ReturnsZeros()
    {
      // Arrange & Act
      var (usage, limit) = CliOutputParser.ParseMemoryUsage("not-a-value / also-invalid");

      // Assert
      Assert.Equal(0, usage);
      Assert.Equal(0, limit);
    }

    #endregion

    #region ParseIOPair Tests

    [Fact]
    public void ParseIOPair_ValidKBPair_ReturnsBothValuesInBytes()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("1.5kB / 2.3kB");

      // Assert
      Assert.Equal((long)(1.5 * 1000), first);   // 1500
      Assert.Equal((long)(2.3 * 1000), second);  // 2300
    }

    [Fact]
    public void ParseIOPair_ValidMiBPair_ReturnsBothValuesInBytes()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("4MiB / 8MiB");

      // Assert
      Assert.Equal(4L * 1024 * 1024, first);   // 4194304
      Assert.Equal(8L * 1024 * 1024, second);  // 8388608
    }

    [Fact]
    public void ParseIOPair_MixedSuffixes_ParsesEachIndependently()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("512kB / 1MiB");

      // Assert
      Assert.Equal(512L * 1000, first);         // 512000 (SI)
      Assert.Equal(1L * 1024 * 1024, second);   // 1048576 (binary)
    }

    [Fact]
    public void ParseIOPair_Null_ReturnsZeros()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair(null);

      // Assert
      Assert.Equal(0, first);
      Assert.Equal(0, second);
    }

    [Fact]
    public void ParseIOPair_EmptyString_ReturnsZeros()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("");

      // Assert
      Assert.Equal(0, first);
      Assert.Equal(0, second);
    }

    [Fact]
    public void ParseIOPair_WhitespaceOnly_ReturnsZeros()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("  \t ");

      // Assert
      Assert.Equal(0, first);
      Assert.Equal(0, second);
    }

    [Fact]
    public void ParseIOPair_SingleValue_ReturnsFirstOnlyAndZeroSecond()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("100kB");

      // Assert
      Assert.Equal(100L * 1000, first);
      Assert.Equal(0, second);
    }

    [Fact]
    public void ParseIOPair_LargeGBValues_ParsesCorrectly()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("10GB / 25GB");

      // Assert
      Assert.Equal(10L * 1000 * 1000 * 1000, first);
      Assert.Equal(25L * 1000 * 1000 * 1000, second);
    }

    [Fact]
    public void ParseIOPair_FractionalValues_ParsesCorrectly()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("0.5MB / 1.25GB");

      // Assert
      Assert.Equal((long)(0.5 * 1000 * 1000), first);
      Assert.Equal((long)(1.25 * 1000 * 1000 * 1000), second);
    }

    [Fact]
    public void ParseIOPair_ZeroValues_ReturnsZeros()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("0B / 0B");

      // Assert
      Assert.Equal(0, first);
      Assert.Equal(0, second);
    }

    [Theory]
    [InlineData("1TB / 2TB", 1000000000000L, 2000000000000L)]
    [InlineData("1TiB / 2TiB", 1099511627776L, 2199023255552L)]
    [InlineData("100B / 200B", 100L, 200L)]
    public void ParseIOPair_VariousSuffixes_ParsesCorrectly(
      string input, long expectedFirst, long expectedSecond)
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair(input);

      // Assert
      Assert.Equal(expectedFirst, first);
      Assert.Equal(expectedSecond, second);
    }

    [Fact]
    public void ParseIOPair_InvalidText_ReturnsZeros()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("abc / xyz");

      // Assert
      Assert.Equal(0, first);
      Assert.Equal(0, second);
    }

    [Fact]
    public void ParseIOPair_RawByteValues_ParsesWithoutSuffix()
    {
      // Arrange & Act
      var (first, second) = CliOutputParser.ParseIOPair("4096 / 8192");

      // Assert
      Assert.Equal(4096, first);
      Assert.Equal(8192, second);
    }

    #endregion
  }
}
