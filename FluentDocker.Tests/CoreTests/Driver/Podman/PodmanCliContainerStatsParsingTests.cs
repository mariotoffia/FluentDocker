using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliContainerDriver stats parsing and byte-value helpers.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliContainerStatsParsingTests
  {
    #region ParseStatsOutput Tests

    [Fact]
    public void ParseStatsOutput_FullPodmanJson_ParsesAllFields()
    {
      var json = @"{
                ""ContainerID"": ""abc123"",
                ""Name"": ""test"",
                ""CPUPerc"": ""5.23%"",
                ""MemUsage"": ""100MiB / 2GiB"",
                ""MemPerc"": ""4.88%"",
                ""NetIO"": ""1.5kB / 2.3kB"",
                ""BlockIO"": ""4MiB / 8MiB"",
                ""PIDs"": ""5""
            }";

      var result = PodmanCliContainerDriver.ParseStatsOutput(json);

      Assert.Equal("abc123", result.ContainerId);
      Assert.Equal("test", result.Name);
      Assert.Equal(5.23, result.CpuPercent, 2);
      Assert.Equal(104857600, result.MemoryUsage);   // 100 * 1024 * 1024
      Assert.Equal(2147483648, result.MemoryLimit);  // 2 * 1024^3
      Assert.Equal(4.88, result.MemoryPercent, 2);
      Assert.Equal(1500, result.NetworkRxBytes);      // 1.5 * 1000
      Assert.Equal(2300, result.NetworkTxBytes);      // 2.3 * 1000
      Assert.Equal(4194304, result.BlockReadBytes);   // 4 * 1024^2
      Assert.Equal(8388608, result.BlockWriteBytes);  // 8 * 1024^2
      Assert.Equal(5, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_JsonArray_ParsesFirst()
    {
      var json = @"[{
                ""ContainerID"": ""abc123"",
                ""Name"": ""test"",
                ""CPUPerc"": ""10.50%"",
                ""MemUsage"": ""50MiB / 1GiB"",
                ""MemPerc"": ""5.00%"",
                ""NetIO"": ""100B / 200B"",
                ""BlockIO"": ""0B / 0B"",
                ""PIDs"": ""3""
            }]";

      var result = PodmanCliContainerDriver.ParseStatsOutput(json);

      Assert.Equal("abc123", result.ContainerId);
      Assert.Equal("test", result.Name);
      Assert.Equal(10.50, result.CpuPercent, 2);
      Assert.Equal(52428800, result.MemoryUsage); // 50 * 1024^2
      Assert.Equal(1073741824, result.MemoryLimit); // 1 * 1024^3
      Assert.Equal(5.00, result.MemoryPercent, 2);
      Assert.Equal(100, result.NetworkRxBytes);
      Assert.Equal(200, result.NetworkTxBytes);
      Assert.Equal(0, result.BlockReadBytes);
      Assert.Equal(0, result.BlockWriteBytes);
      Assert.Equal(3, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_AlternateKeys_Parsed()
    {
      var json = @"{
                ""container_id"": ""def456"",
                ""name"": ""web"",
                ""cpu_perc"": ""2.50%"",
                ""mem_usage"": ""256MiB / 4GiB"",
                ""mem_perc"": ""6.25%"",
                ""net_io"": ""10kB / 20kB"",
                ""block_io"": ""1MiB / 2MiB"",
                ""pids"": ""12""
            }";

      var result = PodmanCliContainerDriver.ParseStatsOutput(json);

      Assert.Equal("def456", result.ContainerId);
      Assert.Equal("web", result.Name);
      Assert.Equal(2.50, result.CpuPercent, 2);
      Assert.Equal(268435456, result.MemoryUsage);   // 256 * 1024^2
      Assert.Equal(4294967296, result.MemoryLimit);  // 4 * 1024^3
      Assert.Equal(6.25, result.MemoryPercent, 2);
      Assert.Equal(10000, result.NetworkRxBytes);     // 10 * 1000
      Assert.Equal(20000, result.NetworkTxBytes);     // 20 * 1000
      Assert.Equal(1048576, result.BlockReadBytes);   // 1 * 1024^2
      Assert.Equal(2097152, result.BlockWriteBytes);  // 2 * 1024^2
      Assert.Equal(12, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_EmptyString_ReturnsEmpty()
    {
      var result = PodmanCliContainerDriver.ParseStatsOutput("");
      Assert.NotNull(result);
      Assert.Null(result.ContainerId);
      Assert.Null(result.Name);
      Assert.Equal(0, result.CpuPercent);
      Assert.Equal(0, result.MemoryUsage);
      Assert.Equal(0, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_ZeroValues_ParsesCorrectly()
    {
      var json = @"{
                ""ContainerID"": ""zero123"",
                ""Name"": ""idle"",
                ""CPUPerc"": ""0.00%"",
                ""MemUsage"": ""0B / 0B"",
                ""MemPerc"": ""0.00%"",
                ""NetIO"": ""0B / 0B"",
                ""BlockIO"": ""0B / 0B"",
                ""PIDs"": ""0""
            }";

      var result = PodmanCliContainerDriver.ParseStatsOutput(json);

      Assert.Equal("zero123", result.ContainerId);
      Assert.Equal("idle", result.Name);
      Assert.Equal(0, result.CpuPercent);
      Assert.Equal(0, result.MemoryUsage);
      Assert.Equal(0, result.MemoryLimit);
      Assert.Equal(0, result.MemoryPercent);
      Assert.Equal(0, result.NetworkRxBytes);
      Assert.Equal(0, result.NetworkTxBytes);
      Assert.Equal(0, result.BlockReadBytes);
      Assert.Equal(0, result.BlockWriteBytes);
      Assert.Equal(0, result.Pids);
    }

    #endregion

    #region ParsePercent Tests

    [Theory]
    [InlineData("5.23%", 5.23)]
    [InlineData("0.00%", 0.0)]
    [InlineData("100%", 100.0)]
    [InlineData("", 0.0)]
    [InlineData(null, 0.0)]
    public void ParsePercent_VariousInputs_ReturnsExpected(string? input, double expected)
    {
      var result = PodmanCliContainerDriver.ParsePercent(input);
      Assert.Equal(expected, result, 2);
    }

    #endregion

    #region ParseByteValue Tests

    [Theory]
    [InlineData("100B", 100L)]
    [InlineData("1.5kB", 1500L)]
    [InlineData("1.5KiB", 1536L)]
    [InlineData("100MiB", 104857600L)]
    [InlineData("2GiB", 2147483648L)]
    [InlineData("1.5GB", 1500000000L)]
    [InlineData("", 0L)]
    [InlineData(null, 0L)]
    public void ParseByteValue_VariousInputs_ReturnsExpected(string? input, long expected)
    {
      var result = PodmanCliContainerDriver.ParseByteValue(input);
      Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1KB", 1000L)]
    [InlineData("1TB", 1000000000000L)]
    [InlineData("1TiB", 1099511627776L)]
    [InlineData("500MB", 500000000L)]
    public void ParseByteValue_AdditionalSuffixes_ReturnsExpected(string input, long expected)
    {
      var result = PodmanCliContainerDriver.ParseByteValue(input);
      Assert.Equal(expected, result);
    }

    #endregion

    #region ParseMemoryUsage Tests

    [Fact]
    public void ParseMemoryUsage_ValidPair_ReturnsBothValues()
    {
      var (usage, limit) = PodmanCliContainerDriver.ParseMemoryUsage("100MiB / 2GiB");
      Assert.Equal(104857600, usage);
      Assert.Equal(2147483648, limit);
    }

    [Fact]
    public void ParseMemoryUsage_NullOrEmpty_ReturnsZeros()
    {
      var (usage1, limit1) = PodmanCliContainerDriver.ParseMemoryUsage(null);
      Assert.Equal(0, usage1);
      Assert.Equal(0, limit1);

      var (usage2, limit2) = PodmanCliContainerDriver.ParseMemoryUsage("");
      Assert.Equal(0, usage2);
      Assert.Equal(0, limit2);
    }

    [Fact]
    public void ParseMemoryUsage_SingleValue_ReturnsUsageOnly()
    {
      var (usage, limit) = PodmanCliContainerDriver.ParseMemoryUsage("100MiB");
      Assert.Equal(104857600, usage);
      Assert.Equal(0, limit);
    }

    #endregion

    #region ParseIOPair Tests

    [Fact]
    public void ParseIOPair_ValidPair_ReturnsBothValues()
    {
      var (first, second) = PodmanCliContainerDriver.ParseIOPair("1.5kB / 2.3kB");
      Assert.Equal(1500, first);
      Assert.Equal(2300, second);
    }

    [Fact]
    public void ParseIOPair_NullOrEmpty_ReturnsZeros()
    {
      var (first1, second1) = PodmanCliContainerDriver.ParseIOPair(null);
      Assert.Equal(0, first1);
      Assert.Equal(0, second1);

      var (first2, second2) = PodmanCliContainerDriver.ParseIOPair("");
      Assert.Equal(0, first2);
      Assert.Equal(0, second2);
    }

    [Fact]
    public void ParseIOPair_MixedSuffixes_ParsesCorrectly()
    {
      var (first, second) = PodmanCliContainerDriver.ParseIOPair("4MiB / 8MiB");
      Assert.Equal(4194304, first);
      Assert.Equal(8388608, second);
    }

    #endregion
  }
}
