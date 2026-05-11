using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for BuildStreamLogsArgs and BuildStreamStatsArgs in both
  /// DockerCliStreamDriver and PodmanCliStreamDriver, verifying that the
  /// Follow/Stream config flags are correctly honored in CLI arguments.
  /// </summary>
  [Trait("Category", "Unit")]
  public class StreamDriverArgsTests
  {
    #region Docker Logs

    [Fact]
    public void Docker_BuildStreamLogsArgs_FollowTrue_ContainsFollowFlag()
    {
      var config = new StreamLogsConfig { Follow = true };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("abc123", config);

      Assert.Contains("-f", result);
      Assert.StartsWith("logs", result);
      Assert.EndsWith("abc123", result);
    }

    [Fact]
    public void Docker_BuildStreamLogsArgs_FollowFalse_DoesNotContainFollowFlag()
    {
      var config = new StreamLogsConfig { Follow = false };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("abc123", config);

      Assert.DoesNotContain("-f", result);
    }

    [Fact]
    public void Docker_BuildStreamLogsArgs_NullConfig_ContainsFollowFlag()
    {
      // Default config has Follow = true
      var result = DockerCliStreamDriver.BuildStreamLogsArgs("abc123", null);

      Assert.Contains("-f", result);
    }

    [Fact]
    public void Docker_BuildStreamLogsArgs_WithTailTimestampsAndSince()
    {
      var config = new StreamLogsConfig
      {
        Follow = true,
        Timestamps = true,
        Tail = 100,
        Since = "1h"
      };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("mycontainer", config);

      Assert.Contains("-t", result);
      Assert.Contains("--tail 100", result);
      Assert.Contains("--since 1h", result);
      Assert.EndsWith("mycontainer", result);
    }

    [Fact]
    public void Docker_BuildStreamLogsArgs_WithUntil_IncludesUntilFlag()
    {
      var config = new StreamLogsConfig { Follow = false, Until = "2024-06-01" };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("c1", config);

      Assert.Contains("--until 2024-06-01", result);
    }

    [Fact]
    public void Docker_BuildStreamLogsArgs_WithDetails_IncludesDetailsFlag()
    {
      var config = new StreamLogsConfig { Follow = false, Details = true };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("c1", config);

      Assert.Contains("--details", result);
    }

    #endregion

    #region Docker Stats

    [Fact]
    public void Docker_BuildStreamStatsArgs_StreamTrue_DoesNotContainNoStream()
    {
      var config = new StreamStatsConfig { Stream = true };

      var result = DockerCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.DoesNotContain("--no-stream", result);
    }

    [Fact]
    public void Docker_BuildStreamStatsArgs_StreamFalse_ContainsNoStream()
    {
      var config = new StreamStatsConfig { Stream = false };

      var result = DockerCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.Contains("--no-stream", result);
    }

    [Fact]
    public void Docker_BuildStreamStatsArgs_NullConfig_DoesNotContainNoStream()
    {
      var result = DockerCliStreamDriver.BuildStreamStatsArgs("abc123", null);

      Assert.DoesNotContain("--no-stream", result);
    }

    [Fact]
    public void Docker_BuildStreamStatsArgs_WithAll_ContainsAllFlag()
    {
      var config = new StreamStatsConfig { All = true };

      var result = DockerCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.Contains("-a", result);
    }

    #endregion

    #region Podman Logs

    [Fact]
    public void Podman_BuildStreamLogsArgs_FollowTrue_ContainsFollowFlag()
    {
      var config = new StreamLogsConfig { Follow = true };

      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("abc123", config);

      Assert.Contains("--follow", result);
      Assert.StartsWith("logs", result);
      Assert.EndsWith("abc123", result);
    }

    [Fact]
    public void Podman_BuildStreamLogsArgs_FollowFalse_DoesNotContainFollowFlag()
    {
      var config = new StreamLogsConfig { Follow = false };

      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("abc123", config);

      Assert.DoesNotContain("--follow", result);
    }

    [Fact]
    public void Podman_BuildStreamLogsArgs_NullConfig_ContainsFollowFlag()
    {
      // Default config has Follow = true
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("abc123", null);

      Assert.Contains("--follow", result);
    }

    [Fact]
    public void Podman_BuildStreamLogsArgs_WithTimestampsTailSinceUntil()
    {
      var config = new StreamLogsConfig
      {
        Follow = false,
        Timestamps = true,
        Tail = 50,
        Since = "2024-01-01",
        Until = "2024-06-01"
      };

      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("pod1", config);

      Assert.DoesNotContain("--follow", result);
      Assert.Contains("--timestamps", result);
      Assert.Contains("--tail 50", result);
      Assert.Contains("--since 2024-01-01", result);
      Assert.Contains("--until 2024-06-01", result);
      Assert.EndsWith("pod1", result);
    }

    #endregion

    #region Podman Stats

    [Fact]
    public void Podman_BuildStreamStatsArgs_StreamFalse_ContainsNoStream()
    {
      var config = new StreamStatsConfig { Stream = false };

      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.Contains("--no-stream", result);
    }

    [Fact]
    public void Podman_BuildStreamStatsArgs_StreamTrue_DoesNotContainNoStream()
    {
      var config = new StreamStatsConfig { Stream = true };

      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.DoesNotContain("--no-stream", result);
    }

    [Fact]
    public void Podman_BuildStreamStatsArgs_NullConfig_DoesNotContainNoStream()
    {
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("abc123", null);

      Assert.DoesNotContain("--no-stream", result);
    }

    [Fact]
    public void Podman_BuildStreamStatsArgs_WithNoHeader_ContainsNoHeaderFlag()
    {
      var config = new StreamStatsConfig { NoHeader = true };

      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.Contains("--no-header", result);
    }

    [Fact]
    public void Podman_BuildStreamStatsArgs_WithAll_ContainsAllFlag()
    {
      var config = new StreamStatsConfig { All = true };

      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("abc123", config);

      Assert.Contains("-a", result);
    }

    #endregion
  }
}
