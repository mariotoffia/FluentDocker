using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliComposeDriver static arg builders and JSON parsing.
  /// Part 1: T3.1–T3.6 (Remove, Parse, Up, Down, Restart, List).
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliComposeArgsTests
  {
    #region T3.1 — BuildRemoveSubArgs

    [Fact]
    public void BuildRemoveSubArgs_Default_NoForceFlag()
    {
      var config = new ComposeRemoveConfig();
      var result = DockerCliComposeDriver.BuildRemoveSubArgs(config);
      Assert.Equal("rm", result);
      Assert.DoesNotContain("-f", result);
    }

    [Fact]
    public void BuildRemoveSubArgs_Force_IncludesForceFlag()
    {
      var config = new ComposeRemoveConfig { Force = true };
      var result = DockerCliComposeDriver.BuildRemoveSubArgs(config);
      Assert.Contains("-f", result);
    }

    [Fact]
    public void BuildRemoveSubArgs_StopAndVolumes_IncludesBothFlags()
    {
      var config = new ComposeRemoveConfig { Stop = true, Volumes = true };
      var result = DockerCliComposeDriver.BuildRemoveSubArgs(config);
      Assert.Contains("-s", result);
      Assert.Contains("-v", result);
      Assert.DoesNotContain("-f", result);
    }

    [Fact]
    public void BuildRemoveSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeRemoveConfig
      {
        Force = true,
        Stop = true,
        Volumes = true
      };
      var result = DockerCliComposeDriver.BuildRemoveSubArgs(config);
      Assert.Contains("-f", result);
      Assert.Contains("-s", result);
      Assert.Contains("-v", result);
    }

    #endregion

    #region T3.2 — ParseServiceList (JSON array + NDJSON)

    [Fact]
    public void ParseServiceList_JsonArray_ReturnsServices()
    {
      var json = @"[
        {""Service"":""web"",""State"":""running"",""ID"":""abc123""},
        {""Service"":""db"",""State"":""exited"",""ID"":""def456""}
      ]";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Equal(2, result.Count);
      Assert.Equal("web", result[0].Name);
      Assert.Equal("running", result[0].State);
      Assert.Equal("abc123", result[0].ContainerId);
      Assert.Equal("db", result[1].Name);
    }

    [Fact]
    public void ParseServiceList_NewlineDelimited_ReturnsServices()
    {
      var json = "{\"Service\":\"web\",\"State\":\"running\",\"ID\":\"abc\"}\n"
               + "{\"Service\":\"db\",\"State\":\"exited\",\"ID\":\"def\"}";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Equal(2, result.Count);
      Assert.Equal("web", result[0].Name);
      Assert.Equal("db", result[1].Name);
    }

    [Fact]
    public void ParseServiceList_Empty_ReturnsEmpty()
    {
      Assert.Empty(DockerCliComposeDriver.ParseServiceList(""));
      Assert.Empty(DockerCliComposeDriver.ParseServiceList(null));
      Assert.Empty(DockerCliComposeDriver.ParseServiceList("  "));
    }

    [Fact]
    public void ParseServiceList_SingleObjectArray_ReturnsSingle()
    {
      var json = @"[{""Service"":""api"",""State"":""running"",""ID"":""x1""}]";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Single(result);
      Assert.Equal("api", result[0].Name);
    }

    [Fact]
    public void ParseServiceList_ScaledService_ReturnsMultipleEntriesSameName()
    {
      // After `docker compose up -d --scale web=3`, ps returns 3 entries
      // with the same Service name but different container IDs/names.
      var json = @"[
        {""Service"":""web"",""State"":""running"",""ID"":""aaa"",""Name"":""proj-web-1""},
        {""Service"":""web"",""State"":""running"",""ID"":""bbb"",""Name"":""proj-web-2""},
        {""Service"":""web"",""State"":""running"",""ID"":""ccc"",""Name"":""proj-web-3""}
      ]";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Equal(3, result.Count);
      Assert.All(result, s => Assert.Equal("web", s.Name));
      // Each container has a distinct ID
      Assert.Equal(3, result.Select(s => s.ContainerId).Distinct().Count());
      // Each container has a distinct container name
      Assert.Equal(3, result.Select(s => s.ContainerName).Distinct().Count());
    }

    [Fact]
    public void ParseServiceList_MixedScaledAndSingle_CorrectCounts()
    {
      // One service scaled to 2, another at 1 instance
      var json = @"[
        {""Service"":""web"",""State"":""running"",""ID"":""a1"",""Name"":""proj-web-1""},
        {""Service"":""web"",""State"":""running"",""ID"":""a2"",""Name"":""proj-web-2""},
        {""Service"":""db"",""State"":""running"",""ID"":""b1"",""Name"":""proj-db-1""}
      ]";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Equal(3, result.Count);
      Assert.Equal(2, result.Count(s => s.Name == "web"));
      Assert.Equal(1, result.Count(s => s.Name == "db"));
    }

    [Fact]
    public void ParseServiceList_ScaledNdjson_ReturnsMultipleEntriesSameName()
    {
      // Same scenario but in newline-delimited JSON format (older compose)
      var json = "{\"Service\":\"api\",\"State\":\"running\",\"ID\":\"x1\",\"Name\":\"p-api-1\"}\n"
               + "{\"Service\":\"api\",\"State\":\"running\",\"ID\":\"x2\",\"Name\":\"p-api-2\"}";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Equal(2, result.Count);
      Assert.All(result, s => Assert.Equal("api", s.Name));
      Assert.NotEqual(result[0].ContainerId, result[1].ContainerId);
    }

    [Fact]
    public void ParseServiceList_ScaledWithMixedStates_AllParsed()
    {
      // After scale-down, some containers may be exited while others running
      var json = @"[
        {""Service"":""web"",""State"":""running"",""ID"":""a1""},
        {""Service"":""web"",""State"":""exited"",""ExitCode"":0,""ID"":""a2""}
      ]";
      var result = DockerCliComposeDriver.ParseServiceList(json);
      Assert.Equal(2, result.Count);
      Assert.Equal("running", result[0].State);
      Assert.Equal("exited", result[1].State);
      Assert.Equal(0, result[1].ExitCode);
    }

    #endregion

    #region T3.3 — BuildUpSubArgs (NoRecreate, NoBuild)

    [Fact]
    public void BuildUpSubArgs_Default_ReturnsUpDetached()
    {
      var config = new ComposeUpConfig();
      var result = DockerCliComposeDriver.BuildUpSubArgs(config);
      Assert.Equal("up -d", result);
    }

    [Fact]
    public void BuildUpSubArgs_NoRecreate_IncludesFlag()
    {
      var config = new ComposeUpConfig { NoRecreate = true };
      var result = DockerCliComposeDriver.BuildUpSubArgs(config);
      Assert.Contains("--no-recreate", result);
    }

    [Fact]
    public void BuildUpSubArgs_NoBuild_IncludesFlag()
    {
      var config = new ComposeUpConfig { NoBuild = true };
      var result = DockerCliComposeDriver.BuildUpSubArgs(config);
      Assert.Contains("--no-build", result);
    }

    [Fact]
    public void BuildUpSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeUpConfig
      {
        Build = true,
        ForceRecreate = true,
        NoRecreate = true,
        RemoveOrphans = true,
        NoBuild = true,
        NoDeps = true,
        NoStart = true,
        Wait = true,
        WaitTimeout = 30,
        Pull = "always",
        Scale = new Dictionary<string, int> { { "web", 3 } },
        Timeout = 60
      };
      var result = DockerCliComposeDriver.BuildUpSubArgs(config);
      Assert.StartsWith("up -d", result);
      Assert.Contains("--build", result);
      Assert.Contains("--force-recreate", result);
      Assert.Contains("--no-recreate", result);
      Assert.Contains("--remove-orphans", result);
      Assert.Contains("--no-build", result);
      Assert.Contains("--no-deps", result);
      Assert.Contains("--no-start", result);
      Assert.Contains("--wait", result);
      Assert.Contains("--wait-timeout 30", result);
      Assert.Contains("--pull always", result);
      Assert.Contains("--scale web=3", result);
      Assert.Contains("--timeout 60", result);
    }

    [Fact]
    public void BuildUpSubArgs_NotDetached_NoDetachFlag()
    {
      var config = new ComposeUpConfig { Detached = false };
      var result = DockerCliComposeDriver.BuildUpSubArgs(config);
      Assert.Equal("up", result);
    }

    #endregion

    #region T3.4 — BuildDownSubArgs (RemoveOrphans)

    [Fact]
    public void BuildDownSubArgs_Default_ReturnsDown()
    {
      var config = new ComposeDownConfig();
      var result = DockerCliComposeDriver.BuildDownSubArgs(config);
      Assert.Equal("down", result);
    }

    [Fact]
    public void BuildDownSubArgs_RemoveOrphans_IncludesFlag()
    {
      var config = new ComposeDownConfig { RemoveOrphans = true };
      var result = DockerCliComposeDriver.BuildDownSubArgs(config);
      Assert.Contains("--remove-orphans", result);
    }

    [Fact]
    public void BuildDownSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeDownConfig
      {
        RemoveVolumes = true,
        RemoveImages = "all",
        RemoveOrphans = true,
        Timeout = 30
      };
      var result = DockerCliComposeDriver.BuildDownSubArgs(config);
      Assert.Contains("--volumes", result);
      Assert.Contains("--rmi all", result);
      Assert.Contains("--remove-orphans", result);
      Assert.Contains("--timeout 30", result);
    }

    #endregion

    #region T3.5 — BuildRestartSubArgs (NoDeps)

    [Fact]
    public void BuildRestartSubArgs_Default_ReturnsRestart()
    {
      var config = new ComposeRestartConfig();
      var result = DockerCliComposeDriver.BuildRestartSubArgs(config);
      Assert.Equal("restart", result);
    }

    [Fact]
    public void BuildRestartSubArgs_NoDeps_IncludesFlag()
    {
      var config = new ComposeRestartConfig { NoDeps = true };
      var result = DockerCliComposeDriver.BuildRestartSubArgs(config);
      Assert.Contains("--no-deps", result);
    }

    [Fact]
    public void BuildRestartSubArgs_TimeoutAndNoDeps_IncludesBoth()
    {
      var config = new ComposeRestartConfig { Timeout = 10, NoDeps = true };
      var result = DockerCliComposeDriver.BuildRestartSubArgs(config);
      Assert.Contains("--timeout 10", result);
      Assert.Contains("--no-deps", result);
    }

    #endregion

    #region T3.6 — BuildListSubArgs (Status)

    [Fact]
    public void BuildListSubArgs_Default_ReturnsBaseCommand()
    {
      var config = new ComposeListConfig();
      var result = DockerCliComposeDriver.BuildListSubArgs(config);
      Assert.Equal("ps --format json", result);
    }

    [Fact]
    public void BuildListSubArgs_Status_IncludesFilterFlag()
    {
      var config = new ComposeListConfig { Status = "running" };
      var result = DockerCliComposeDriver.BuildListSubArgs(config);
      Assert.Contains("--filter status=running", result);
    }

    [Fact]
    public void BuildListSubArgs_AllAndQuiet_IncludesFlags()
    {
      var config = new ComposeListConfig { All = true, Quiet = true };
      var result = DockerCliComposeDriver.BuildListSubArgs(config);
      Assert.Contains(" -a", result);
      Assert.Contains(" -q", result);
    }

    [Fact]
    public void BuildListSubArgs_AllFields_IncludesAll()
    {
      var config = new ComposeListConfig
      {
        All = true,
        Quiet = true,
        Status = "exited"
      };
      var result = DockerCliComposeDriver.BuildListSubArgs(config);
      Assert.Contains(" -a", result);
      Assert.Contains(" -q", result);
      Assert.Contains("--filter status=exited", result);
    }

    #endregion
  }
}
