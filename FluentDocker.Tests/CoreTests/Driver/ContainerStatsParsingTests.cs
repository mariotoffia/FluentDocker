using System.Reflection;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for container stats parsing (CliOutputParser and DockerCliContainerDriver).
  /// </summary>
  [Trait("Category", "Unit")]
  public class ContainerStatsParsingTests
  {
    [Theory]
    [InlineData("0B", 0)]
    [InlineData("100B", 100)]
    [InlineData("1kB", 1000)]
    [InlineData("1KB", 1000)]
    [InlineData("1KiB", 1024)]
    [InlineData("1.5kB", 1500)]
    [InlineData("1MB", 1000000)]
    [InlineData("1MiB", 1048576)]
    [InlineData("1.5MiB", 1572864)]
    [InlineData("1GB", 1000000000)]
    [InlineData("1GiB", 1073741824)]
    [InlineData("7.8GiB", 8375186227)]  // 7.8 * 1024^3, approximate
    [InlineData("1TB", 1000000000000)]
    [InlineData("1TiB", 1099511627776)]
    public void ParseByteValue_ParsesCorrectly(string input, long expected)
    {
      var result = CliOutputParser.ParseByteValue(input);

      // Allow for floating point rounding (larger tolerance for large numbers)
      var tolerance = Math.Max(1, expected / 1000000);  // 0.0001% tolerance
      Assert.InRange(result, expected - tolerance, expected + tolerance);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("invalid", 0)]
    public void ParseByteValue_HandlesInvalidInput(string? input, long expected)
    {
      var result = CliOutputParser.ParseByteValue(input);
      Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0.00%", 0.0)]
    [InlineData("25.50%", 25.5)]
    [InlineData("100%", 100.0)]
    [InlineData("0.01%", 0.01)]
    [InlineData("99.99%", 99.99)]
    public void ParsePercent_ParsesCorrectly(string input, double expected)
    {
      var result = CliOutputParser.ParsePercent(input);
      Assert.Equal(expected, result, 2);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("invalid", 0.0)]
    public void ParsePercent_HandlesInvalidInput(string? input, double expected)
    {
      var result = CliOutputParser.ParsePercent(input);
      Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseStatsOutput_ParsesValidJson()
    {
      var json = @"{""BlockIO"":""2.1MB / 1.5MB"",""CPUPerc"":""25.50%"",""Container"":""abc123"",""ID"":""abc123def456"",""MemPerc"":""9.77%"",""MemUsage"":""100MiB / 1GiB"",""Name"":""test-container"",""NetIO"":""1.2kB / 512B"",""PIDs"":""5""}";

      var method = typeof(DockerCliContainerDriver).GetMethod(
          "ParseStatsOutput",
          BindingFlags.NonPublic | BindingFlags.Static);

      Assert.NotNull(method);

      var result = (ContainerStatsResult)method.Invoke(null, new object[] { json, "abc123" });

      Assert.NotNull(result);
      Assert.Equal("abc123", result.ContainerId);
      Assert.Equal("test-container", result.Name);
      Assert.Equal(25.5, result.CpuPercent, 1);
      Assert.Equal(9.77, result.MemoryPercent, 2);
      Assert.Equal(5, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_HandlesInvalidJson()
    {
      var invalidJson = "not valid json";

      var method = typeof(DockerCliContainerDriver).GetMethod(
          "ParseStatsOutput",
          BindingFlags.NonPublic | BindingFlags.Static);

      Assert.NotNull(method);

      var result = (ContainerStatsResult)method.Invoke(null, new object[] { invalidJson, "abc123" });

      // Should not throw, just return empty stats with container ID
      Assert.NotNull(result);
      Assert.Equal("abc123", result.ContainerId);
    }

    [Fact]
    public void ParseStatsOutput_HandlesMissingFields()
    {
      // JSON with only some fields
      var partialJson = @"{""Name"":""test"",""CPUPerc"":""10%""}";

      var method = typeof(DockerCliContainerDriver).GetMethod(
          "ParseStatsOutput",
          BindingFlags.NonPublic | BindingFlags.Static);

      Assert.NotNull(method);

      var result = (ContainerStatsResult)method.Invoke(null, new object[] { partialJson, "abc123" });

      Assert.NotNull(result);
      Assert.Equal("abc123", result.ContainerId);
      Assert.Equal("test", result.Name);
      Assert.Equal(10.0, result.CpuPercent, 1);
      Assert.Equal(0, result.MemoryUsage);  // Missing field defaults to 0
      Assert.Equal(0, result.Pids);  // Missing field defaults to 0
    }

    [Fact]
    public void ContainerStatsResult_HasExpectedProperties()
    {
      var stats = new ContainerStatsResult
      {
        ContainerId = "test-id",
        Name = "test-name",
        CpuPercent = 50.0,
        MemoryUsage = 1024,
        MemoryLimit = 2048,
        MemoryPercent = 50.0,
        NetworkRxBytes = 100,
        NetworkTxBytes = 200,
        BlockReadBytes = 300,
        BlockWriteBytes = 400,
        Pids = 10
      };

      Assert.Equal("test-id", stats.ContainerId);
      Assert.Equal("test-name", stats.Name);
      Assert.Equal(50.0, stats.CpuPercent);
      Assert.Equal(1024, stats.MemoryUsage);
      Assert.Equal(2048, stats.MemoryLimit);
      Assert.Equal(50.0, stats.MemoryPercent);
      Assert.Equal(100, stats.NetworkRxBytes);
      Assert.Equal(200, stats.NetworkTxBytes);
      Assert.Equal(300, stats.BlockReadBytes);
      Assert.Equal(400, stats.BlockWriteBytes);
      Assert.Equal(10, stats.Pids);
    }
  }
}
