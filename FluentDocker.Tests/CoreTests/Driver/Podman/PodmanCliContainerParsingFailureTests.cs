using System;
using FluentDocker.Drivers.Podman.Cli.Components;
using Newtonsoft.Json;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests that Podman parsing methods throw on malformed JSON
  /// instead of silently returning empty/partial data.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliContainerParsingFailureTests
  {
    #region ParseContainerList — Failure Cases

    [Fact]
    public void ParseContainerList_EmptyString_ReturnsEmptyList()
    {
      var result = PodmanCliContainerDriver.ParseContainerList("");
      Assert.Empty(result);
    }

    [Fact]
    public void ParseContainerList_WhitespaceOnly_ReturnsEmptyList()
    {
      var result = PodmanCliContainerDriver.ParseContainerList("   \n  ");
      Assert.Empty(result);
    }

    [Fact]
    public void ParseContainerList_Null_ReturnsEmptyList()
    {
      var result = PodmanCliContainerDriver.ParseContainerList(null);
      Assert.Empty(result);
    }

    [Fact]
    public void ParseContainerList_MalformedJson_Throws()
    {
      Assert.ThrowsAny<JsonException>(
          () => PodmanCliContainerDriver.ParseContainerList("{not valid json"));
    }

    [Fact]
    public void ParseContainerList_MalformedNdjson_Throws()
    {
      var ndjson = "{\"Id\":\"abc\"}\n{broken}";
      Assert.ThrowsAny<JsonException>(
          () => PodmanCliContainerDriver.ParseContainerList(ndjson));
    }

    [Fact]
    public void ParseContainerList_MalformedArray_Throws()
    {
      Assert.ThrowsAny<JsonException>(
          () => PodmanCliContainerDriver.ParseContainerList("[{\"Id\":\"a\"}, broken]"));
    }

    #endregion

    #region ParseContainerInspect — Failure Cases

    [Fact]
    public void ParseContainerInspect_MalformedJson_Throws()
    {
      Assert.ThrowsAny<Exception>(
          () => PodmanCliContainerDriver.ParseContainerInspect("{not valid}"));
    }

    [Fact]
    public void ParseContainerInspect_EmptyString_Throws()
    {
      Assert.ThrowsAny<Exception>(
          () => PodmanCliContainerDriver.ParseContainerInspect(""));
    }

    [Fact]
    public void ParseContainerInspect_EmptyArray_Throws()
    {
      Assert.ThrowsAny<Exception>(
          () => PodmanCliContainerDriver.ParseContainerInspect("[]"));
    }

    [Fact]
    public void ParseContainerInspect_ValidJson_DoesNotThrow()
    {
      var json = @"[{""Id"":""abc123"",""Name"":""test""}]";
      var container = PodmanCliContainerDriver.ParseContainerInspect(json);
      Assert.Equal("abc123", container.Id);
      Assert.Equal("test", container.Name);
    }

    #endregion
  }
}
