using System;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests that Podman CLI output parsers (FIX-7) fail loudly with diagnostics on non-empty
  /// but unparseable output instead of silently returning an empty/zeroed success, while
  /// legitimately-empty output still yields an empty result.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliParserStrictnessTests
  {
    [Fact]
    public void ParseStatsOutput_Empty_ReturnsDefault()
    {
      var result = PodmanCliContainerDriver.ParseStatsOutput("");
      Assert.NotNull(result);
      Assert.Equal(0, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_Whitespace_ReturnsDefault()
    {
      var result = PodmanCliContainerDriver.ParseStatsOutput("   \n  ");
      Assert.NotNull(result);
      Assert.Equal(0, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_EmptyJsonArray_ReturnsDefault()
    {
      var result = PodmanCliContainerDriver.ParseStatsOutput("[]");
      Assert.NotNull(result);
      Assert.Equal(0, result.Pids);
    }

    [Fact]
    public void ParseStatsOutput_NonEmptyGarbage_Throws()
    {
      var ex = Assert.Throws<FluentDockerException>(() =>
          PodmanCliContainerDriver.ParseStatsOutput("this is not json"));
      Assert.Contains("Failed to parse Podman container stats output", ex.Message);
    }

    [Fact]
    public void ParseContainerList_Empty_ReturnsEmptyList()
    {
      var list = PodmanCliContainerDriver.ParseContainerList("");
      Assert.NotNull(list);
      Assert.Empty(list);
    }

    [Fact]
    public void ParseContainerList_NonEmptyGarbage_Throws()
    {
      // Invalid non-empty output must surface as an exception, not an empty success list.
      Assert.ThrowsAny<Exception>(() =>
          PodmanCliContainerDriver.ParseContainerList("this is not json at all"));
    }

    [Fact]
    public void ParseContainerList_ValidJsonArray_Parses()
    {
      var list = PodmanCliContainerDriver.ParseContainerList(
          "[{\"Id\":\"abc123\",\"Image\":\"nginx\",\"Names\":[\"web\"],\"State\":\"running\"}]");
      Assert.Single(list);
      Assert.Equal("abc123", list[0].Id);
      Assert.Equal("web", list[0].Name);
    }
  }
}
