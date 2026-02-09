using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public class StreamDriverStatsParsingTests
  {
    [Fact]
    public void Docker_ParseStreamStatsLine_FullCliJson_ParsesAllFields()
    {
      // Arrange - Docker CLI stats JSON format with keys like CPUPerc, MemUsage, etc.
      const string json = "{" +
          "\"ID\":\"abc123\"," +
          "\"Name\":\"web\"," +
          "\"CPUPerc\":\"5.23%\"," +
          "\"MemPerc\":\"10.50%\"," +
          "\"MemUsage\":\"100MiB / 1GiB\"," +
          "\"NetIO\":\"1.5kB / 2.3kB\"," +
          "\"BlockIO\":\"500kB / 1MB\"," +
          "\"PIDs\":\"15\"" +
          "}";

      // Act
      var stats = DockerCliStreamDriver.ParseStreamStatsLine(json);

      // Assert
      Assert.NotNull(stats);
      Assert.Equal("abc123", stats.ContainerId);
      Assert.Equal("web", stats.Name);
      Assert.Equal(5.23, stats.CpuPercentage, 2);
      Assert.Equal(10.50, stats.MemoryPercentage, 2);

      // 100 MiB = 100 * 1024 * 1024 = 104857600
      Assert.Equal(104857600L, stats.MemoryUsage);
      // 1 GiB = 1024 * 1024 * 1024 = 1073741824
      Assert.Equal(1073741824L, stats.MemoryLimit);

      // 1.5 kB = 1500
      Assert.Equal(1500L, stats.NetworkRx);
      // 2.3 kB = 2300
      Assert.Equal(2300L, stats.NetworkTx);

      // 500 kB = 500000
      Assert.Equal(500000L, stats.BlockRead);
      // 1 MB = 1000000
      Assert.Equal(1000000L, stats.BlockWrite);

      Assert.Equal(15, stats.Pids);
      Assert.Equal(json, stats.RawJson);
    }

    [Fact]
    public void Docker_ParseStreamStatsLine_MinimalJson_ParsesAvailableFields()
    {
      // Arrange - minimal JSON with only Name
      const string json = "{\"Name\":\"web\"}";

      // Act
      var stats = DockerCliStreamDriver.ParseStreamStatsLine(json);

      // Assert
      Assert.NotNull(stats);
      Assert.Equal("web", stats.Name);
      Assert.Null(stats.ContainerId);
      Assert.Equal(0.0, stats.CpuPercentage);
      Assert.Equal(0.0, stats.MemoryPercentage);
      Assert.Equal(0L, stats.MemoryUsage);
      Assert.Equal(0L, stats.MemoryLimit);
      Assert.Equal(0L, stats.NetworkRx);
      Assert.Equal(0L, stats.NetworkTx);
      Assert.Equal(0L, stats.BlockRead);
      Assert.Equal(0L, stats.BlockWrite);
      Assert.Equal(0, stats.Pids);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Docker_ParseStreamStatsLine_EmptyOrNull_ReturnsNull(string input)
    {
      // Act
      var result = DockerCliStreamDriver.ParseStreamStatsLine(input);

      // Assert
      Assert.Null(result);
    }

    [Fact]
    public void Podman_ParseStats_FullJson_ParsesAllFields()
    {
      // Arrange - Podman stats JSON format
      const string json = "{" +
          "\"ContainerID\":\"abc123\"," +
          "\"Name\":\"web\"," +
          "\"CPUPerc\":\"5.23%\"," +
          "\"MemPerc\":\"10.50%\"," +
          "\"MemUsage\":\"100MiB / 1GiB\"," +
          "\"NetIO\":\"1.5kB / 2.3kB\"," +
          "\"BlockIO\":\"500kB / 1MB\"," +
          "\"PIDs\":\"15\"" +
          "}";

      // Act
      var stats = PodmanCliStreamDriver.ParseStats(json);

      // Assert
      Assert.NotNull(stats);
      Assert.Equal("abc123", stats.ContainerId);
      Assert.Equal("web", stats.Name);
      Assert.Equal(5.23, stats.CpuPercentage, 2);
      Assert.Equal(10.50, stats.MemoryPercentage, 2);

      // 100 MiB = 104857600
      Assert.Equal(104857600L, stats.MemoryUsage);
      // 1 GiB = 1073741824
      Assert.Equal(1073741824L, stats.MemoryLimit);

      // 1.5 kB = 1500
      Assert.Equal(1500L, stats.NetworkRx);
      // 2.3 kB = 2300
      Assert.Equal(2300L, stats.NetworkTx);

      // 500 kB = 500000
      Assert.Equal(500000L, stats.BlockRead);
      // 1 MB = 1000000
      Assert.Equal(1000000L, stats.BlockWrite);

      Assert.Equal(15, stats.Pids);
      Assert.Equal(json, stats.RawJson);
    }

    [Fact]
    public void Podman_ParseStats_ContainerKey_ParsesId()
    {
      // Arrange - Podman may also use "Container" as key
      const string json = "{\"ContainerID\":\"def456\",\"Name\":\"db\",\"CPUPerc\":\"1.00%\"}";

      // Act
      var stats = PodmanCliStreamDriver.ParseStats(json);

      // Assert
      Assert.NotNull(stats);
      Assert.Equal("def456", stats.ContainerId);
      Assert.Equal("db", stats.Name);
      Assert.Equal(1.0, stats.CpuPercentage, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Podman_ParseStats_EmptyOrNull_ReturnsNull(string input)
    {
      // Act
      var result = PodmanCliStreamDriver.ParseStats(input);

      // Assert
      Assert.Null(result);
    }
  }
}
