using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliStreamDriver: BuildStreamLogsArgs and BuildStreamEventsArgs.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class PodmanCliStreamDriverTests
  {
    #region BuildStreamLogsArgs Tests

    [Fact]
    public void BuildStreamLogsArgs_DefaultConfig_IncludesFollowByDefault()
    {
      // StreamLogsConfig.Follow defaults to true
      var config = new StreamLogsConfig();
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Equal("logs --follow ctr1", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_FollowFalse_OmitsFollowFlag()
    {
      var config = new StreamLogsConfig { Follow = false };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Equal("logs ctr1", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_FollowTrue_IncludesFollow()
    {
      var config = new StreamLogsConfig { Follow = true };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--follow", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_TimestampsTrue_IncludesTimestamps()
    {
      var config = new StreamLogsConfig { Follow = false, Timestamps = true };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--timestamps", result);
      Assert.Equal("logs --timestamps ctr1", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_Tail100_IncludesTail()
    {
      var config = new StreamLogsConfig { Follow = false, Tail = 100 };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--tail 100", result);
      Assert.Equal("logs --tail 100 ctr1", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_Since_IncludesSinceArg()
    {
      var config = new StreamLogsConfig { Follow = false, Since = "2024-01-01" };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--since 2024-01-01", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_Until_IncludesUntilArg()
    {
      var config = new StreamLogsConfig { Follow = false, Until = "2024-12-31" };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--until 2024-12-31", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_AllOptionsCombined_IncludesAllFlags()
    {
      var config = new StreamLogsConfig
      {
        Follow = true,
        Timestamps = true,
        Tail = 50,
        Since = "2024-01-01T00:00:00Z",
        Until = "2024-12-31T23:59:59Z"
      };

      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("my-container", config);

      Assert.StartsWith("logs", result);
      Assert.Contains("--follow", result);
      Assert.Contains("--timestamps", result);
      Assert.Contains("--tail 50", result);
      Assert.Contains("--since 2024-01-01T00:00:00Z", result);
      Assert.Contains("--until 2024-12-31T23:59:59Z", result);
      Assert.EndsWith("my-container", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_NullConfig_UsesDefaults()
    {
      // null config gets replaced by new StreamLogsConfig() which has Follow=true
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", null);

      Assert.Equal("logs --follow ctr1", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_ContainerIdAlwaysAppended()
    {
      var config = new StreamLogsConfig { Follow = false };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("abc-def-123", config);

      Assert.EndsWith(" abc-def-123", result);
    }

    #endregion

    #region BuildStreamEventsArgs Tests

    [Fact]
    public void BuildStreamEventsArgs_NullConfig_ReturnsBaseCommand()
    {
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(null);

      Assert.Equal("events --format json", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_DefaultConfig_ReturnsBaseCommand()
    {
      var config = new StreamEventsConfig();
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Equal("events --format json", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_Since_IncludesSinceArg()
    {
      var config = new StreamEventsConfig { Since = "2024-06-01" };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--since 2024-06-01", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_Until_IncludesUntilArg()
    {
      var config = new StreamEventsConfig { Until = "2024-12-31" };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--until 2024-12-31", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_SinceAndUntil_IncludesBothTimeFilters()
    {
      var config = new StreamEventsConfig
      {
        Since = "2024-01-01",
        Until = "2024-12-31"
      };

      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--since 2024-01-01", result);
      Assert.Contains("--until 2024-12-31", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_TypeFilters_AddsFilterTypeEntries()
    {
      var config = new StreamEventsConfig
      {
        Types = new List<string> { "container", "image" }
      };

      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--filter type=container", result);
      Assert.Contains("--filter type=image", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_ActionFilters_AddsFilterEventEntries()
    {
      var config = new StreamEventsConfig
      {
        Actions = new List<string> { "start", "stop", "die" }
      };

      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--filter event=start", result);
      Assert.Contains("--filter event=stop", result);
      Assert.Contains("--filter event=die", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_CustomFilters_AddsKeyValuePairs()
    {
      var config = new StreamEventsConfig
      {
        Filters = new Dictionary<string, string>
        {
          { "container", "my-app" },
          { "image", "nginx" }
        }
      };

      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--filter container=my-app", result);
      Assert.Contains("--filter image=nginx", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_CombinedFilters_IncludesAllFilters()
    {
      var config = new StreamEventsConfig
      {
        Since = "2024-01-01",
        Until = "2024-12-31",
        Types = new List<string> { "container" },
        Actions = new List<string> { "start", "stop" },
        Filters = new Dictionary<string, string>
        {
          { "name", "web-server" }
        }
      };

      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.StartsWith("events --format json", result);
      Assert.Contains("--since 2024-01-01", result);
      Assert.Contains("--until 2024-12-31", result);
      Assert.Contains("--filter type=container", result);
      Assert.Contains("--filter event=start", result);
      Assert.Contains("--filter event=stop", result);
      Assert.Contains("--filter name=web-server", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_EmptyCollections_NoExtraFilters()
    {
      var config = new StreamEventsConfig
      {
        Types = new List<string>(),
        Actions = new List<string>(),
        Filters = new Dictionary<string, string>()
      };

      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Equal("events --format json", result);
    }

    #endregion
  }
}
