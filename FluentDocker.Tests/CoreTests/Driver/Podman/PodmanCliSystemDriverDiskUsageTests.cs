using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliSystemDriver disk usage parsing.
  /// Validates ParseDiskUsageOutput against Podman's JSON output formats:
  /// JSON arrays, newline-delimited JSON, numeric and string sizes.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliSystemDriverDiskUsageTests
  {
    #region JSON Array Parsing

    [Fact]
    public void ParseDiskUsageOutput_JsonArray_NumericSizes_ParsesCorrectly()
    {
      var output = @"[
                {""Type"":""Images"",""Total"":5,""Active"":3,""Size"":1234567890,""Reclaimable"":500000000},
                {""Type"":""Containers"",""Total"":3,""Active"":1,""Size"":100000000,""Reclaimable"":50000000},
                {""Type"":""Volumes"",""Total"":2,""Active"":1,""Size"":200000000,""Reclaimable"":100000000}
            ]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      // Images
      Assert.Equal(5, info.Images.TotalCount);
      Assert.Equal(3, info.Images.Active);
      Assert.Equal(1_234_567_890L, info.Images.Size);
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
    }

    [Fact]
    public void ParseDiskUsageOutput_JsonArray_TotalSizeAndReclaimable_AreSums()
    {
      var output = @"[
                {""Type"":""Images"",""Total"":1,""Active"":0,""Size"":1000,""Reclaimable"":500},
                {""Type"":""Containers"",""Total"":1,""Active"":0,""Size"":2000,""Reclaimable"":1000},
                {""Type"":""Volumes"",""Total"":1,""Active"":0,""Size"":3000,""Reclaimable"":1500}
            ]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(6000L, info.TotalSize);
      Assert.Equal(3000L, info.Reclaimable);
    }

    [Fact]
    public void ParseDiskUsageOutput_JsonArray_StringSizes_ParsesCorrectly()
    {
      var output = @"[
                {""Type"":""Images"",""Total"":2,""Active"":1,""Size"":""1.5GB"",""Reclaimable"":""500MB""},
                {""Type"":""Containers"",""Total"":1,""Active"":0,""Size"":""100MB"",""Reclaimable"":""100MB""}
            ]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(2, info.Images.TotalCount);
      Assert.Equal(1_500_000_000L, info.Images.Size);
      Assert.Equal(500_000_000L, info.Images.Reclaimable);
      Assert.Equal(1, info.Containers.TotalCount);
      Assert.Equal(100_000_000L, info.Containers.Size);
    }

    [Fact]
    public void ParseDiskUsageOutput_JsonArray_ReclaimableWithPercentage_StripsPercent()
    {
      var output = @"[
                {""Type"":""Images"",""Total"":1,""Active"":0,""Size"":""1GB"",""Reclaimable"":""500MB (50%)""}
            ]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(500_000_000L, info.Images.Reclaimable);
    }

    #endregion

    #region Newline-Delimited Parsing

    [Fact]
    public void ParseDiskUsageOutput_NewlineDelimited_NumericSizes_ParsesCorrectly()
    {
      var output = string.Join("\n",
          "{\"Type\":\"Images\",\"Total\":4,\"Active\":2,\"Size\":999999,\"Reclaimable\":444444}",
          "{\"Type\":\"Containers\",\"Total\":2,\"Active\":1,\"Size\":555555,\"Reclaimable\":222222}",
          "{\"Type\":\"Volumes\",\"Total\":1,\"Active\":0,\"Size\":111111,\"Reclaimable\":111111}"
      );

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(4, info.Images.TotalCount);
      Assert.Equal(999_999L, info.Images.Size);
      Assert.Equal(2, info.Containers.TotalCount);
      Assert.Equal(555_555L, info.Containers.Size);
      Assert.Equal(1, info.Volumes.TotalCount);
      Assert.Equal(111_111L, info.Volumes.Size);
    }

    [Fact]
    public void ParseDiskUsageOutput_NewlineDelimited_WindowsLineEndings()
    {
      var output = "{\"Type\":\"Images\",\"Total\":1,\"Active\":0,\"Size\":500,\"Reclaimable\":100}\r\n" +
                   "{\"Type\":\"Volumes\",\"Total\":2,\"Active\":1,\"Size\":300,\"Reclaimable\":50}";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(1, info.Images.TotalCount);
      Assert.Equal(500L, info.Images.Size);
      Assert.Equal(2, info.Volumes.TotalCount);
      Assert.Equal(300L, info.Volumes.Size);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ParseDiskUsageOutput_EmptyString_ReturnsEmptyInfo()
    {
      var info = PodmanCliSystemDriver.ParseDiskUsageOutput("");

      Assert.NotNull(info);
      Assert.Equal(0, info.TotalSize);
      Assert.Equal(0, info.Reclaimable);
      Assert.Equal(0, info.Images.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_NullInput_ReturnsEmptyInfo()
    {
      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(null);

      Assert.NotNull(info);
      Assert.Equal(0, info.TotalSize);
    }

    [Fact]
    public void ParseDiskUsageOutput_EmptyJsonArray_ReturnsEmptyInfo()
    {
      var info = PodmanCliSystemDriver.ParseDiskUsageOutput("[]");

      Assert.NotNull(info);
      Assert.Equal(0, info.TotalSize);
      Assert.Equal(0, info.Images.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_MalformedLine_SkipsIt()
    {
      var output = string.Join("\n",
          "{\"Type\":\"Images\",\"Total\":3,\"Active\":1,\"Size\":1000,\"Reclaimable\":500}",
          "this is not json at all",
          "{\"Type\":\"Containers\",\"Total\":1,\"Active\":0,\"Size\":200,\"Reclaimable\":100}"
      );

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(3, info.Images.TotalCount);
      Assert.Equal(1, info.Containers.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_NoBuildCache_BuildCacheRemainsDefault()
    {
      var output = @"[
                {""Type"":""Images"",""Total"":1,""Active"":0,""Size"":1000,""Reclaimable"":500},
                {""Type"":""Containers"",""Total"":1,""Active"":0,""Size"":500,""Reclaimable"":250},
                {""Type"":""Volumes"",""Total"":1,""Active"":0,""Size"":300,""Reclaimable"":150}
            ]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(0, info.BuildCache.TotalCount);
      Assert.Equal(0, info.BuildCache.Size);
      Assert.Equal(0, info.BuildCache.Reclaimable);
    }

    [Fact]
    public void ParseDiskUsageOutput_LocalVolumesType_MapsToVolumes()
    {
      // Some Podman versions might use "Local Volumes" like Docker
      var output = @"[{""Type"":""Local Volumes"",""Total"":5,""Active"":2,""Size"":999,""Reclaimable"":333}]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(5, info.Volumes.TotalCount);
      Assert.Equal(2, info.Volumes.Active);
      Assert.Equal(999L, info.Volumes.Size);
    }

    [Fact]
    public void ParseDiskUsageOutput_TotalCountKey_AlsoHandled()
    {
      // TotalCount key used by some versions instead of Total
      var output = @"[{""Type"":""Images"",""TotalCount"":7,""Active"":3,""Size"":5000,""Reclaimable"":2000}]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(7, info.Images.TotalCount);
    }

    [Fact]
    public void ParseDiskUsageOutput_MixedNumericAndStringSizes_Parses()
    {
      var output = @"[
                {""Type"":""Images"",""Total"":2,""Active"":1,""Size"":1234567,""Reclaimable"":""500kB""},
                {""Type"":""Containers"",""Total"":1,""Active"":0,""Size"":""50MiB"",""Reclaimable"":25000}
            ]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(1_234_567L, info.Images.Size);
      Assert.Equal(500_000L, info.Images.Reclaimable);
      Assert.Equal(52_428_800L, info.Containers.Size); // 50 * 1024 * 1024
      Assert.Equal(25_000L, info.Containers.Reclaimable);
    }

    [Fact]
    public void ParseDiskUsageOutput_ZeroValues_ParsesCorrectly()
    {
      var output = @"[{""Type"":""Images"",""Total"":0,""Active"":0,""Size"":0,""Reclaimable"":0}]";

      var info = PodmanCliSystemDriver.ParseDiskUsageOutput(output);

      Assert.Equal(0, info.Images.TotalCount);
      Assert.Equal(0, info.Images.Active);
      Assert.Equal(0L, info.Images.Size);
      Assert.Equal(0L, info.Images.Reclaimable);
    }

    #endregion
  }
}
