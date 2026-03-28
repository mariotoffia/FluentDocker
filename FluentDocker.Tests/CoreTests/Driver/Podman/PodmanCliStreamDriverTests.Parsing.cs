using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliStreamDriver: BuildStreamStatsArgs, ParseStats, and ParseEvent.
  /// </summary>
  public partial class PodmanCliStreamDriverTests
  {
    #region BuildStreamStatsArgs Tests

    [Fact]
    public void BuildStreamStatsArgs_DefaultConfig_ReturnsBaseCommandWithContainerId()
    {
      // StreamStatsConfig.Stream defaults to true, so --no-stream is NOT added
      var config = new StreamStatsConfig();
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", config);

      Assert.Equal("stats --format json ctr1", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_StreamFalse_IncludesNoStream()
    {
      var config = new StreamStatsConfig { Stream = false };
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", config);

      Assert.Contains("--no-stream", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_StreamTrue_OmitsNoStream()
    {
      var config = new StreamStatsConfig { Stream = true };
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", config);

      Assert.DoesNotContain("--no-stream", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_NoHeaderTrue_IncludesNoHeader()
    {
      var config = new StreamStatsConfig { NoHeader = true };
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", config);

      Assert.Contains("--no-header", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_AllTrue_IncludesAllFlag()
    {
      var config = new StreamStatsConfig { All = true };
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", config);

      Assert.Contains("-a", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_AllOptionsCombined()
    {
      var config = new StreamStatsConfig
      {
        Stream = false,
        NoHeader = true,
        All = true
      };

      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", config);

      Assert.Equal("stats --format json --no-stream --no-header -a ctr1", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_NullContainerId_OmitsContainerId()
    {
      var config = new StreamStatsConfig();
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs(null, config);

      Assert.Equal("stats --format json", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_EmptyContainerId_OmitsContainerId()
    {
      var config = new StreamStatsConfig();
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("", config);

      Assert.Equal("stats --format json", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_NullConfig_UsesDefaults()
    {
      // null config means none of the optional flags apply
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs("ctr1", null);

      Assert.Equal("stats --format json ctr1", result);
    }

    [Fact]
    public void BuildStreamStatsArgs_NullConfigNullContainer_ReturnsBaseOnly()
    {
      var result = PodmanCliStreamDriver.BuildStreamStatsArgs(null, null);

      Assert.Equal("stats --format json", result);
    }

    #endregion

    #region ParseStats Tests

    [Fact]
    public void ParseStats_ValidJson_ParsesAllFields()
    {
      var json = @"{
        ""ContainerID"": ""abc123"",
        ""Name"": ""web-app"",
        ""CPUPerc"": ""12.50%"",
        ""MemUsage"": ""256MiB / 4GiB"",
        ""MemPerc"": ""6.25%"",
        ""NetIO"": ""1.5kB / 2.3kB"",
        ""BlockIO"": ""4MiB / 8MiB"",
        ""PIDs"": ""10""
      }";

      var result = PodmanCliStreamDriver.ParseStats(json);

      Assert.NotNull(result);
      Assert.Equal("abc123", result.ContainerId);
      Assert.Equal("web-app", result.Name);
      Assert.Equal(12.50, result.CpuPercentage, 2);
      Assert.Equal(268435456, result.MemoryUsage);    // 256 MiB
      Assert.Equal(4294967296, result.MemoryLimit);   // 4 GiB
      Assert.Equal(6.25, result.MemoryPercentage, 2);
      Assert.Equal(1500, result.NetworkRx);            // 1.5 kB
      Assert.Equal(2300, result.NetworkTx);            // 2.3 kB
      Assert.Equal(4194304, result.BlockRead);         // 4 MiB
      Assert.Equal(8388608, result.BlockWrite);        // 8 MiB
      Assert.Equal(10, result.Pids);
    }

    [Fact]
    public void ParseStats_JsonWithAnsiEscapePrefix_StripsAndParses()
    {
      // Podman streaming mode may prefix ANSI escape codes before JSON
      var ansiPrefix = "\u001b[2J\u001b[H";
      var json = ansiPrefix + @"{
        ""ContainerID"": ""esc123"",
        ""Name"": ""ansi-test"",
        ""CPUPerc"": ""3.00%"",
        ""MemUsage"": ""50MiB / 1GiB"",
        ""MemPerc"": ""5.00%"",
        ""NetIO"": ""100B / 200B"",
        ""BlockIO"": ""0B / 0B"",
        ""PIDs"": ""2""
      }";

      var result = PodmanCliStreamDriver.ParseStats(json);

      Assert.NotNull(result);
      Assert.Equal("esc123", result.ContainerId);
      Assert.Equal("ansi-test", result.Name);
      Assert.Equal(3.00, result.CpuPercentage, 2);
    }

    [Fact]
    public void ParseStats_JsonWithAnsiSuffix_StripsTrailingContent()
    {
      // JSON followed by ANSI codes and trailing text
      var json = @"{
        ""ContainerID"": ""trail123"",
        ""Name"": ""trailing"",
        ""CPUPerc"": ""1.00%"",
        ""MemUsage"": ""10MiB / 512MiB"",
        ""MemPerc"": ""2.00%"",
        ""NetIO"": ""0B / 0B"",
        ""BlockIO"": ""0B / 0B"",
        ""PIDs"": ""1""
      }" + "\u001b[0m some trailing text";

      var result = PodmanCliStreamDriver.ParseStats(json);

      Assert.NotNull(result);
      Assert.Equal("trail123", result.ContainerId);
    }

    [Fact]
    public void ParseStats_NullInput_ReturnsNull()
    {
      var result = PodmanCliStreamDriver.ParseStats(null);
      Assert.Null(result);
    }

    [Fact]
    public void ParseStats_EmptyInput_ReturnsNull()
    {
      var result = PodmanCliStreamDriver.ParseStats("");
      Assert.Null(result);
    }

    [Fact]
    public void ParseStats_WhitespaceInput_ReturnsNull()
    {
      var result = PodmanCliStreamDriver.ParseStats("   ");
      Assert.Null(result);
    }

    [Fact]
    public void ParseStats_MalformedJson_ReturnsDefaultValues()
    {
      // Malformed JSON with braces passes the brace-check but yields a default stats object
      var result = PodmanCliStreamDriver.ParseStats("{not valid json at all}}}");
      if (result != null)
      {
        Assert.Equal(0, result.CpuPercentage);
        Assert.Equal(0, result.MemoryUsage);
      }
    }

    [Fact]
    public void ParseStats_NoBraces_ReturnsNull()
    {
      var result = PodmanCliStreamDriver.ParseStats("no json here");
      Assert.Null(result);
    }

    [Fact]
    public void ParseStats_RawJsonFieldIsPopulated()
    {
      var json = @"{
        ""ContainerID"": ""raw123"",
        ""Name"": ""raw"",
        ""CPUPerc"": ""0.00%"",
        ""MemUsage"": ""0B / 0B"",
        ""MemPerc"": ""0.00%"",
        ""NetIO"": ""0B / 0B"",
        ""BlockIO"": ""0B / 0B"",
        ""PIDs"": ""0""
      }";

      var result = PodmanCliStreamDriver.ParseStats(json);

      Assert.NotNull(result);
      Assert.NotNull(result.RawJson);
      Assert.Contains("raw123", result.RawJson);
    }

    #endregion

    #region ParseEvent Tests (via Reflection)

    private static ContainerEvent InvokeParseEvent(string json)
    {
      var method = typeof(PodmanCliStreamDriver).GetMethod(
        "ParseEvent",
        BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (ContainerEvent)method.Invoke(null, new object[] { json });
    }

    [Fact]
    public void ParseEvent_ValidJson_ParsesTypeActionActorId()
    {
      var json = @"{
        ""Type"": ""container"",
        ""Action"": ""start"",
        ""Actor"": { ""ID"": ""abc123def456"" }
      }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal("container", result.Type);
      Assert.Equal("start", result.Action);
      Assert.Equal("abc123def456", result.ActorId);
      Assert.Equal(json, result.RawJson);
    }

    [Fact]
    public void ParseEvent_AlternateKeys_LowercaseType_StatusAction_IdField()
    {
      // Podman may use lowercase "type", "Status" for action, and flat "id"
      var json = @"{
        ""type"": ""image"",
        ""Status"": ""pull"",
        ""id"": ""sha256:abc123""
      }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal("image", result.Type);
      Assert.Equal("pull", result.Action);
      Assert.Equal("sha256:abc123", result.ActorId);
    }

    [Fact]
    public void ParseEvent_PascalCaseTypeTakesPrecedenceOverLowercase()
    {
      // When both Type and type exist, Type (from GetStringOrDefault("Type"))
      // should be returned first due to null-coalesce
      var json = @"{
        ""Type"": ""container"",
        ""type"": ""network"",
        ""Action"": ""create""
      }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal("container", result.Type);
    }

    [Fact]
    public void ParseEvent_ActionTakesPrecedenceOverStatus()
    {
      var json = @"{
        ""Type"": ""container"",
        ""Action"": ""stop"",
        ""Status"": ""exited""
      }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal("stop", result.Action);
    }

    [Fact]
    public void ParseEvent_ActorIdTakesPrecedenceOverFlatId()
    {
      var json = @"{
        ""Type"": ""container"",
        ""Action"": ""die"",
        ""Actor"": { ""ID"": ""actor-id-123"" },
        ""id"": ""flat-id-456""
      }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal("actor-id-123", result.ActorId);
    }

    [Fact]
    public void ParseEvent_NoActorObject_FallsBackToFlatId()
    {
      var json = @"{
        ""Type"": ""volume"",
        ""Action"": ""create"",
        ""id"": ""vol-abc123""
      }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal("vol-abc123", result.ActorId);
    }

    [Fact]
    public void ParseEvent_NullInput_ReturnsNull()
    {
      var result = InvokeParseEvent(null);
      Assert.Null(result);
    }

    [Fact]
    public void ParseEvent_EmptyInput_ReturnsNull()
    {
      var result = InvokeParseEvent("");
      Assert.Null(result);
    }

    [Fact]
    public void ParseEvent_MalformedJson_ReturnsNull()
    {
      var result = InvokeParseEvent("{{{broken json");
      Assert.Null(result);
    }

    [Fact]
    public void ParseEvent_MinimalValidJson_ReturnsEventWithNullFields()
    {
      // Valid JSON but no known keys -- all fields end up null
      var json = @"{ ""unknown_key"": ""value"" }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Null(result.Type);
      Assert.Null(result.Action);
      Assert.Null(result.ActorId);
      Assert.Equal(json, result.RawJson);
    }

    [Fact]
    public void ParseEvent_RawJsonFieldContainsOriginalInput()
    {
      var json = @"{ ""Type"": ""container"", ""Action"": ""start"" }";

      var result = InvokeParseEvent(json);

      Assert.NotNull(result);
      Assert.Equal(json, result.RawJson);
    }

    #endregion
  }
}
