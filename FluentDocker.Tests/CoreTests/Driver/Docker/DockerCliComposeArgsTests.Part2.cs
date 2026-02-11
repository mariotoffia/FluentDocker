using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliComposeDriver static arg builders.
  /// Part 2: T3.7–T3.13 (Logs, Config, Build, Pull, Run, Scale, Create).
  /// </summary>
  public partial class DockerCliComposeArgsTests
  {
    #region T3.7 — BuildLogsSubArgs (Until, NoColor)

    [Fact]
    public void BuildLogsSubArgs_Default_ReturnsLogs()
    {
      var config = new ComposeLogsConfig();
      var result = DockerCliComposeDriver.BuildLogsSubArgs(config);
      Assert.Equal("logs", result);
    }

    [Fact]
    public void BuildLogsSubArgs_Until_IncludesFlag()
    {
      var config = new ComposeLogsConfig { Until = "2024-12-31" };
      var result = DockerCliComposeDriver.BuildLogsSubArgs(config);
      Assert.Contains("--until 2024-12-31", result);
    }

    [Fact]
    public void BuildLogsSubArgs_NoColor_IncludesFlag()
    {
      var config = new ComposeLogsConfig { NoColor = true };
      var result = DockerCliComposeDriver.BuildLogsSubArgs(config);
      Assert.Contains("--no-color", result);
    }

    [Fact]
    public void BuildLogsSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeLogsConfig
      {
        Follow = true,
        Timestamps = true,
        Tail = 100,
        Since = "1h",
        Until = "now",
        NoColor = true
      };
      var result = DockerCliComposeDriver.BuildLogsSubArgs(config);
      Assert.Contains(" -f", result);
      Assert.Contains(" -t", result);
      Assert.Contains("--tail 100", result);
      Assert.Contains("--since 1h", result);
      Assert.Contains("--until now", result);
      Assert.Contains("--no-color", result);
    }

    #endregion

    #region T3.8 — BuildConfigSubArgs (ResolveImageDigests, Format)

    [Fact]
    public void BuildConfigSubArgs_Default_ReturnsConfig()
    {
      var config = new ComposeConfigConfig();
      var result = DockerCliComposeDriver.BuildConfigSubArgs(config);
      Assert.Equal("config", result);
    }

    [Fact]
    public void BuildConfigSubArgs_ResolveImageDigests_IncludesFlag()
    {
      var config = new ComposeConfigConfig { ResolveImageDigests = true };
      var result = DockerCliComposeDriver.BuildConfigSubArgs(config);
      Assert.Contains("--resolve-image-digests", result);
    }

    [Fact]
    public void BuildConfigSubArgs_Format_IncludesFlag()
    {
      var config = new ComposeConfigConfig { Format = "yaml" };
      var result = DockerCliComposeDriver.BuildConfigSubArgs(config);
      Assert.Contains("--format yaml", result);
    }

    [Fact]
    public void BuildConfigSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeConfigConfig
      {
        ShowServices = true,
        ShowVolumes = true,
        ResolveImageDigests = true,
        Format = "json"
      };
      var result = DockerCliComposeDriver.BuildConfigSubArgs(config);
      Assert.Contains("--services", result);
      Assert.Contains("--volumes", result);
      Assert.Contains("--resolve-image-digests", result);
      Assert.Contains("--format json", result);
    }

    #endregion

    #region T3.9 — BuildBuildSubArgs (BuildArgs)

    [Fact]
    public void BuildBuildSubArgs_Default_ReturnsBuild()
    {
      var config = new ComposeBuildConfig();
      var result = DockerCliComposeDriver.BuildBuildSubArgs(config);
      Assert.Equal("build", result);
    }

    [Fact]
    public void BuildBuildSubArgs_BuildArgs_IncludesFlags()
    {
      var config = new ComposeBuildConfig
      {
        BuildArgs = new Dictionary<string, string>
        {
          { "VERSION", "1.0" },
          { "DEBUG", "true" }
        }
      };
      var result = DockerCliComposeDriver.BuildBuildSubArgs(config);
      Assert.Contains("--build-arg VERSION=1.0", result);
      Assert.Contains("--build-arg DEBUG=true", result);
    }

    [Fact]
    public void BuildBuildSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeBuildConfig
      {
        NoCache = true,
        Pull = true,
        ForceRm = true,
        Parallel = true,
        BuildArgs = new Dictionary<string, string> { { "V", "1" } }
      };
      var result = DockerCliComposeDriver.BuildBuildSubArgs(config);
      Assert.Contains("--no-cache", result);
      Assert.Contains("--pull", result);
      Assert.Contains("--force-rm", result);
      Assert.Contains("--parallel", result);
      Assert.Contains("--build-arg V=1", result);
    }

    #endregion

    #region T3.10 — BuildPullSubArgs (IncludeDeps)

    [Fact]
    public void BuildPullSubArgs_Default_ReturnsPull()
    {
      var config = new ComposePullConfig();
      var result = DockerCliComposeDriver.BuildPullSubArgs(config);
      Assert.Equal("pull", result);
    }

    [Fact]
    public void BuildPullSubArgs_IncludeDeps_IncludesFlag()
    {
      var config = new ComposePullConfig { IncludeDeps = true };
      var result = DockerCliComposeDriver.BuildPullSubArgs(config);
      Assert.Contains("--include-deps", result);
    }

    [Fact]
    public void BuildPullSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposePullConfig
      {
        Quiet = true,
        IgnorePullFailures = true,
        IncludeDeps = true
      };
      var result = DockerCliComposeDriver.BuildPullSubArgs(config);
      Assert.Contains(" -q", result);
      Assert.Contains("--ignore-pull-failures", result);
      Assert.Contains("--include-deps", result);
    }

    #endregion

    #region T3.11 — BuildRunSubArgs (Entrypoint, WorkDir, etc.)

    [Fact]
    public void BuildRunSubArgs_MinimalConfig_ReturnsRunWithService()
    {
      var config = new ComposeRunConfig { Service = "web" };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Equal("run web", result);
    }

    [Fact]
    public void BuildRunSubArgs_Entrypoint_IncludesFlag()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        Entrypoint = "/bin/sh"
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Contains("--entrypoint /bin/sh", result);
    }

    [Fact]
    public void BuildRunSubArgs_WorkDir_IncludesFlag()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        WorkDir = "/app"
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Contains("-w /app", result);
    }

    [Fact]
    public void BuildRunSubArgs_ServicePorts_IncludesFlag()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        ServicePorts = true
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Contains("--service-ports", result);
    }

    [Fact]
    public void BuildRunSubArgs_Publish_IncludesFlags()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        Publish = new List<string> { "8080:80", "443:443" }
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Contains("-p 8080:80", result);
      Assert.Contains("-p 443:443", result);
    }

    [Fact]
    public void BuildRunSubArgs_Volumes_IncludesFlags()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        Volumes = new List<string> { "/data:/app/data", "/logs:/app/logs" }
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Contains("-v /data:/app/data", result);
      Assert.Contains("-v /logs:/app/logs", result);
    }

    [Fact]
    public void BuildRunSubArgs_TtyFalse_IncludesNoTtyFlag()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        Tty = false
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.Contains(" -T", result);
    }

    [Fact]
    public void BuildRunSubArgs_TtyTrue_NoNoTtyFlag()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        Tty = true
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.DoesNotContain("-T", result);
    }

    [Fact]
    public void BuildRunSubArgs_DefaultTty_IsTrue()
    {
      var config = new ComposeRunConfig { Service = "web" };
      Assert.True(config.Tty);
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.DoesNotContain("-T", result);
    }

    [Fact]
    public void BuildRunSubArgs_AllFlags_ProducesCorrectArgs()
    {
      var config = new ComposeRunConfig
      {
        Service = "web",
        Detach = true,
        Rm = true,
        NoDeps = true,
        Name = "test-run",
        User = "root",
        Entrypoint = "/bin/bash",
        WorkDir = "/app",
        ServicePorts = true,
        Publish = new List<string> { "8080:80" },
        Volumes = new List<string> { "/data:/data" },
        Tty = false,
        Command = new[] { "echo", "hello" }
      };
      var result = DockerCliComposeDriver.BuildRunSubArgs(config);
      Assert.StartsWith("run", result);
      Assert.Contains(" -d", result);
      Assert.Contains("--rm", result);
      Assert.Contains("--no-deps", result);
      Assert.Contains("--name test-run", result);
      Assert.Contains("-u root", result);
      Assert.Contains("--entrypoint /bin/bash", result);
      Assert.Contains("-w /app", result);
      Assert.Contains("--service-ports", result);
      Assert.Contains("-p 8080:80", result);
      Assert.Contains("-v /data:/data", result);
      Assert.Contains(" -T", result);
      Assert.EndsWith("web echo hello", result);
    }

    #endregion

    #region T3.12 — BuildScaleSubArgs (NoDeps)

    [Fact]
    public void BuildScaleSubArgs_Default_ReturnsUpDetached()
    {
      var config = new ComposeScaleConfig
      {
        Scale = new Dictionary<string, int> { { "web", 3 } }
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.StartsWith("up -d", result);
      Assert.Contains("--scale web=3", result);
    }

    [Fact]
    public void BuildScaleSubArgs_NoDeps_IncludesFlag()
    {
      var config = new ComposeScaleConfig
      {
        NoDeps = true,
        Scale = new Dictionary<string, int> { { "web", 2 } }
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.Contains("--no-deps", result);
      Assert.Contains("--scale web=2", result);
    }

    [Fact]
    public void BuildScaleSubArgs_MultipleServices_IncludesAllScales()
    {
      var config = new ComposeScaleConfig
      {
        Scale = new Dictionary<string, int>
        {
          { "web", 3 },
          { "worker", 5 }
        }
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.Contains("--scale web=3", result);
      Assert.Contains("--scale worker=5", result);
    }

    [Fact]
    public void BuildScaleSubArgs_EmptyScale_ReturnsUpDetachedOnly()
    {
      var config = new ComposeScaleConfig
      {
        Scale = new Dictionary<string, int>()
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.Equal("up -d", result);
    }

    [Fact]
    public void BuildScaleSubArgs_ScaleToZero_ProducesZeroReplica()
    {
      var config = new ComposeScaleConfig
      {
        Scale = new Dictionary<string, int> { { "worker", 0 } }
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.Contains("--scale worker=0", result);
    }

    [Fact]
    public void BuildScaleSubArgs_LargeReplicaCount_ProducesCorrectValue()
    {
      var config = new ComposeScaleConfig
      {
        Scale = new Dictionary<string, int> { { "api", 100 } }
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.Contains("--scale api=100", result);
    }

    [Fact]
    public void BuildScaleSubArgs_NoDepsWithMultipleServices_CombinesFlags()
    {
      var config = new ComposeScaleConfig
      {
        NoDeps = true,
        Scale = new Dictionary<string, int>
        {
          { "web", 3 },
          { "worker", 2 },
          { "cache", 1 }
        }
      };
      var result = DockerCliComposeDriver.BuildScaleSubArgs(config);
      Assert.StartsWith("up -d", result);
      Assert.Contains("--no-deps", result);
      Assert.Contains("--scale web=3", result);
      Assert.Contains("--scale worker=2", result);
      Assert.Contains("--scale cache=1", result);
    }

    #endregion

    #region T3.13 — BuildCreateSubArgs (Pull, RemoveOrphans)

    [Fact]
    public void BuildCreateSubArgs_Default_ReturnsCreate()
    {
      var config = new ComposeCreateConfig();
      var result = DockerCliComposeDriver.BuildCreateSubArgs(config);
      Assert.Equal("create", result);
    }

    [Fact]
    public void BuildCreateSubArgs_Pull_IncludesFlag()
    {
      var config = new ComposeCreateConfig { Pull = "always" };
      var result = DockerCliComposeDriver.BuildCreateSubArgs(config);
      Assert.Contains("--pull always", result);
    }

    [Fact]
    public void BuildCreateSubArgs_RemoveOrphans_IncludesFlag()
    {
      var config = new ComposeCreateConfig { RemoveOrphans = true };
      var result = DockerCliComposeDriver.BuildCreateSubArgs(config);
      Assert.Contains("--remove-orphans", result);
    }

    [Fact]
    public void BuildCreateSubArgs_AllFlags_IncludesAll()
    {
      var config = new ComposeCreateConfig
      {
        Build = true,
        ForceRecreate = true,
        NoRecreate = true,
        NoBuild = true,
        Pull = "missing",
        RemoveOrphans = true
      };
      var result = DockerCliComposeDriver.BuildCreateSubArgs(config);
      Assert.Contains("--build", result);
      Assert.Contains("--force-recreate", result);
      Assert.Contains("--no-recreate", result);
      Assert.Contains("--no-build", result);
      Assert.Contains("--pull missing", result);
      Assert.Contains("--remove-orphans", result);
    }

    #endregion
  }
}
