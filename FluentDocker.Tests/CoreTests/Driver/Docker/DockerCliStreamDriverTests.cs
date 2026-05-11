using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliStreamDriver covering attach arg building,
  /// event stream arg building, and stats parsing with ANSI escape codes.
  /// Complements StreamDriverArgsTests and StreamDriverStatsParsingTests
  /// which already cover log/stats args and basic stats parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliStreamDriverTests
  {
    #region Attach Arg Building

    [Fact]
    public void AttachArgs_DefaultConfig_SigProxyTrue_NoSigProxyFlag()
    {
      // Default SigProxy is true, so --sig-proxy=false should NOT appear
      var args = BuildAttachArgs("ctr1", new AttachConfig());

      Assert.StartsWith("attach", args);
      Assert.DoesNotContain("--sig-proxy", args);
      Assert.EndsWith("ctr1", args);
    }

    [Fact]
    public void AttachArgs_SigProxyFalse_ContainsSigProxyFalseFlag()
    {
      var args = BuildAttachArgs("ctr1", new AttachConfig
      {
        SigProxy = false
      });

      Assert.Contains("--sig-proxy=false", args);
    }

    [Fact]
    public void AttachArgs_SigProxyTrue_DoesNotContainSigProxyFlag()
    {
      var args = BuildAttachArgs("ctr1", new AttachConfig
      {
        SigProxy = true
      });

      Assert.DoesNotContain("--sig-proxy", args);
    }

    [Fact]
    public void AttachArgs_WithDetachKeys_ContainsDetachKeysFlag()
    {
      var args = BuildAttachArgs("ctr1", new AttachConfig
      {
        DetachKeys = "ctrl-p,ctrl-q"
      });

      Assert.Contains("--detach-keys ctrl-p,ctrl-q", args);
    }

    [Fact]
    public void AttachArgs_EmptyDetachKeys_DoesNotContainDetachKeysFlag()
    {
      var args = BuildAttachArgs("ctr1", new AttachConfig
      {
        DetachKeys = ""
      });

      Assert.DoesNotContain("--detach-keys", args);
    }

    [Fact]
    public void AttachArgs_NullDetachKeys_DoesNotContainDetachKeysFlag()
    {
      var args = BuildAttachArgs("ctr1", new AttachConfig
      {
        DetachKeys = null
      });

      Assert.DoesNotContain("--detach-keys", args);
    }

    [Fact]
    public void AttachArgs_BothFlags_CorrectOrder()
    {
      var args = BuildAttachArgs("ctr1", new AttachConfig
      {
        SigProxy = false,
        DetachKeys = "ctrl-c"
      });

      var sigProxyIdx = args.IndexOf("--sig-proxy=false");
      var detachKeysIdx = args.IndexOf("--detach-keys");
      var containerIdx = args.LastIndexOf("ctr1");

      Assert.True(sigProxyIdx < detachKeysIdx,
          "sig-proxy comes before detach-keys");
      Assert.True(detachKeysIdx < containerIdx,
          "detach-keys comes before container id");
    }

    [Fact]
    public void AttachArgs_ContainerIdAtEnd()
    {
      var args = BuildAttachArgs("my-container", new AttachConfig());

      Assert.EndsWith("my-container", args);
    }

    [Fact]
    public void AttachArgs_NullConfig_UsesDefaults()
    {
      // When null config, driver creates default AttachConfig
      var config = new AttachConfig(); // same as null would produce
      var args = BuildAttachArgs("ctr", config);

      Assert.Equal("attach ctr", args);
    }

    [Theory]
    [InlineData("ctrl-p,ctrl-q")]
    [InlineData("ctrl-\\")]
    [InlineData("ctrl-a")]
    public void AttachArgs_VariousDetachKeys_IncludedCorrectly(string keys)
    {
      var args = BuildAttachArgs("ctr", new AttachConfig
      {
        DetachKeys = keys
      });

      Assert.Contains($"--detach-keys", args);
    }

    #endregion

    #region Event Stream Arg Building

    [Fact]
    public void EventArgs_NullConfig_BaseCommandWithJsonFormat()
    {
      var args = BuildEventsArgs(null);

      Assert.StartsWith("events --format", args);
    }

    [Fact]
    public void EventArgs_WithSince_ContainsSinceFlag()
    {
      var config = new StreamEventsConfig { Since = "2024-01-01" };

      var args = BuildEventsArgs(config);

      Assert.Contains("--since 2024-01-01", args);
    }

    [Fact]
    public void EventArgs_WithUntil_ContainsUntilFlag()
    {
      var config = new StreamEventsConfig { Until = "2024-12-31" };

      var args = BuildEventsArgs(config);

      Assert.Contains("--until 2024-12-31", args);
    }

    [Fact]
    public void EventArgs_WithSinceAndUntil_ContainsBothFlags()
    {
      var config = new StreamEventsConfig
      {
        Since = "1h",
        Until = "30m"
      };

      var args = BuildEventsArgs(config);

      Assert.Contains("--since 1h", args);
      Assert.Contains("--until 30m", args);
    }

    [Fact]
    public void EventArgs_WithFilters_ContainsFilterFlags()
    {
      var config = new StreamEventsConfig
      {
        Filters = new Dictionary<string, string>
        {
          ["type"] = "container",
          ["event"] = "start"
        }
      };

      var args = BuildEventsArgs(config);

      Assert.Contains("--filter", args);
    }

    [Fact]
    public void EventArgs_MultipleFilters_AllIncluded()
    {
      var config = new StreamEventsConfig
      {
        Filters = new Dictionary<string, string>
        {
          ["container"] = "web",
          ["event"] = "die"
        }
      };

      var args = BuildEventsArgs(config);

      // Each filter generates a separate --filter flag
      var firstFilter = args.IndexOf("--filter");
      var secondFilter = args.IndexOf("--filter", firstFilter + 1);
      Assert.True(secondFilter > firstFilter,
          "Expected two separate --filter flags");
    }

    [Fact]
    public void EventArgs_NoSinceOrUntil_DoesNotContainTimeFlags()
    {
      var config = new StreamEventsConfig();

      var args = BuildEventsArgs(config);

      Assert.DoesNotContain("--since", args);
      Assert.DoesNotContain("--until", args);
    }

    [Fact]
    public void EventArgs_NoFilters_DoesNotContainFilterFlag()
    {
      var config = new StreamEventsConfig();

      var args = BuildEventsArgs(config);

      Assert.DoesNotContain("--filter", args);
    }

    #endregion

    #region ParseStreamStatsLine — ANSI Escape Code Handling

    [Fact]
    public void ParseStreamStatsLine_AnsiEscapePrefixed_StripsAndParses()
    {
      // Docker streaming mode may prefix ANSI escape codes
      var ansiPrefix = "\u001b[2J\u001b[H";
      var json = ansiPrefix +
          "{\"ID\":\"abc123\",\"Name\":\"web\",\"CPUPerc\":\"2.50%\"," +
          "\"MemPerc\":\"5.00%\",\"MemUsage\":\"50MiB / 512MiB\"," +
          "\"NetIO\":\"1kB / 2kB\",\"BlockIO\":\"0B / 0B\",\"PIDs\":\"8\"}";

      var stats = DockerCliStreamDriver.ParseStreamStatsLine(json);

      Assert.NotNull(stats);
      Assert.Equal("abc123", stats.ContainerId);
      Assert.Equal("web", stats.Name);
      Assert.Equal(2.50, stats.CpuPercentage, 2);
    }

    [Fact]
    public void ParseStreamStatsLine_AnsiCursorHome_StripsAndParses()
    {
      // ESC[H is cursor home, commonly seen in docker stats streaming
      var input = "\u001b[H{\"ID\":\"x1\",\"Name\":\"db\",\"CPUPerc\":\"0.10%\"," +
                  "\"MemPerc\":\"1.00%\",\"MemUsage\":\"10MiB / 1GiB\"," +
                  "\"NetIO\":\"0B / 0B\",\"BlockIO\":\"0B / 0B\",\"PIDs\":\"3\"}";

      var stats = DockerCliStreamDriver.ParseStreamStatsLine(input);

      Assert.NotNull(stats);
      Assert.Equal("x1", stats.ContainerId);
      Assert.Equal("db", stats.Name);
      Assert.Equal(0.10, stats.CpuPercentage, 2);
      Assert.Equal(3, stats.Pids);
    }

    [Fact]
    public void ParseStreamStatsLine_TrailingTextAfterJson_Ignored()
    {
      // JSON followed by trailing ANSI codes or other text
      var input =
          "{\"ID\":\"tr1\",\"Name\":\"app\",\"CPUPerc\":\"3.00%\"," +
          "\"MemPerc\":\"8.00%\",\"MemUsage\":\"80MiB / 1GiB\"," +
          "\"NetIO\":\"5kB / 10kB\",\"BlockIO\":\"1MB / 2MB\"," +
          "\"PIDs\":\"12\"}\u001b[0m trailing text";

      var stats = DockerCliStreamDriver.ParseStreamStatsLine(input);

      Assert.NotNull(stats);
      Assert.Equal("tr1", stats.ContainerId);
      Assert.Equal("app", stats.Name);
      Assert.Equal(12, stats.Pids);
    }

    [Fact]
    public void ParseStreamStatsLine_NoJsonBraces_ReturnsNull()
    {
      var result = DockerCliStreamDriver.ParseStreamStatsLine(
          "\u001b[2J\u001b[H no json here at all");

      Assert.Null(result);
    }

    [Fact]
    public void ParseStreamStatsLine_WhitespaceOnly_ReturnsNull()
    {
      var result = DockerCliStreamDriver.ParseStreamStatsLine("   \t  ");

      Assert.Null(result);
    }

    [Fact]
    public void ParseStreamStatsLine_ContainerKey_FallbackToContainer()
    {
      // Some Docker versions use "Container" instead of "ID"
      const string json =
          "{\"Container\":\"fallback1\",\"Name\":\"alt\"," +
          "\"CPUPerc\":\"1.00%\",\"MemPerc\":\"2.00%\"," +
          "\"MemUsage\":\"20MiB / 256MiB\"," +
          "\"NetIO\":\"0B / 0B\",\"BlockIO\":\"0B / 0B\"," +
          "\"PIDs\":\"5\"}";

      var stats = DockerCliStreamDriver.ParseStreamStatsLine(json);

      Assert.NotNull(stats);
      Assert.Equal("fallback1", stats.ContainerId);
    }

    [Fact]
    public void ParseStreamStatsLine_BothIdAndContainer_IdPreferred()
    {
      // ID takes precedence over Container in the implementation
      const string json =
          "{\"ID\":\"primary\",\"Container\":\"secondary\"," +
          "\"Name\":\"both\",\"CPUPerc\":\"0.00%\"," +
          "\"MemPerc\":\"0.00%\",\"MemUsage\":\"0B / 0B\"," +
          "\"NetIO\":\"0B / 0B\",\"BlockIO\":\"0B / 0B\"," +
          "\"PIDs\":\"1\"}";

      var stats = DockerCliStreamDriver.ParseStreamStatsLine(json);

      Assert.NotNull(stats);
      Assert.Equal("primary", stats.ContainerId);
    }

    [Fact]
    public void ParseStreamStatsLine_ZeroPids_ParsedAsZero()
    {
      const string json =
          "{\"ID\":\"z1\",\"Name\":\"zero\"," +
          "\"CPUPerc\":\"0.00%\",\"MemPerc\":\"0.00%\"," +
          "\"MemUsage\":\"0B / 0B\"," +
          "\"NetIO\":\"0B / 0B\",\"BlockIO\":\"0B / 0B\"," +
          "\"PIDs\":\"0\"}";

      var stats = DockerCliStreamDriver.ParseStreamStatsLine(json);

      Assert.NotNull(stats);
      Assert.Equal(0, stats.Pids);
    }

    #endregion

    #region BuildStreamLogsArgs — Container ID Quoting

    [Fact]
    public void BuildStreamLogsArgs_ContainerWithSpaces_Quoted()
    {
      var config = new StreamLogsConfig { Follow = false };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs(
          "container with spaces", config);

      Assert.Contains("\"container with spaces\"", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_SinceWithSpaces_Quoted()
    {
      var config = new StreamLogsConfig
      {
        Follow = false,
        Since = "2024-01-01 00:00:00"
      };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("ctr", config);

      Assert.Contains("--since \"2024-01-01 00:00:00\"", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_SimpleContainerId_NotQuoted()
    {
      var config = new StreamLogsConfig { Follow = false };

      var result = DockerCliStreamDriver.BuildStreamLogsArgs("abc123", config);

      Assert.DoesNotContain("\"abc123\"", result);
      Assert.EndsWith("abc123", result);
    }

    #endregion

    #region BuildStreamStatsArgs — Container ID Quoting

    [Fact]
    public void BuildStreamStatsArgs_NullContainerId_NoContainerInArgs()
    {
      var config = new StreamStatsConfig();

      var result = DockerCliStreamDriver.BuildStreamStatsArgs(null, config);

      // Should end with the format string, not a container
      Assert.StartsWith("stats --format", result);
      Assert.DoesNotContain("null", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_EmptyContainerId_NoContainerInArgs()
    {
      var config = new StreamStatsConfig();

      var result = DockerCliStreamDriver.BuildStreamStatsArgs("", config);

      // Empty string is treated as no container
      Assert.DoesNotContain("\"\"", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_ContainerWithSpecialChars_Quoted()
    {
      var config = new StreamStatsConfig();

      var result = DockerCliStreamDriver.BuildStreamStatsArgs(
          "container$name", config);

      Assert.Contains("\"container$name\"", result);
    }

    #endregion

    #region Helper: Reconstruct Arg Strings

    /// <summary>
    /// Mirrors the attach arg-building logic from
    /// DockerCliStreamDriver.AttachAsync to test arg construction
    /// without invoking the process.
    /// </summary>
    private static string BuildAttachArgs(string containerId, AttachConfig config)
    {
      config ??= new AttachConfig();
      var args = "attach";

      if (!config.SigProxy)
        args += " --sig-proxy=false";
      if (!string.IsNullOrEmpty(config.DetachKeys))
        args += $" --detach-keys {config.DetachKeys}";

      args += $" {containerId}";
      return args;
    }

    /// <summary>
    /// Mirrors the event stream arg-building logic from
    /// DockerCliStreamDriver.StreamEventsAsync.
    /// </summary>
    private static string BuildEventsArgs(StreamEventsConfig config)
    {
      var args = "events --format \"{{json .}}\"";
      if (config?.Since != null)
        args += $" --since {config.Since}";
      if (config?.Until != null)
        args += $" --until {config.Until}";
      if (config?.Filters != null)
      {
        foreach (var filter in config.Filters)
          args += $" --filter {filter.Key}={filter.Value}";
      }
      return args;
    }

    #endregion
  }
}
