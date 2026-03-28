using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliImageDriver: BuildBuildArgs, ParseSize,
  /// image removal output parsing, and image load output parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliImageDriverTests
  {
    #region BuildBuildArgs - Tags

    [Fact]
    public void BuildBuildArgs_SingleTag_IncludesTag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/src",
        Tags = { "myapp:latest" }
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      Assert.Contains("--tag myapp:latest", result);
    }

    [Fact]
    public void BuildBuildArgs_MultipleTags_IncludesAllTags()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/src",
        Tags = { "myapp:latest", "myapp:v1.0", "registry.io/myapp:v1.0" }
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--tag myapp:latest", result);
      Assert.Contains("--tag myapp:v1.0", result);
      Assert.Contains("--tag registry.io/myapp:v1.0", result);
    }

    [Fact]
    public void BuildBuildArgs_NoTags_OmitsTagFlag()
    {
      var config = new ImageBuildConfig { BuildContext = "/src" };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--tag", result);
    }

    #endregion

    #region BuildBuildArgs - BuildArgs

    [Fact]
    public void BuildBuildArgs_SingleBuildArg_IncludesIt()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        BuildArgs = { { "VERSION", "1.0" } }
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--build-arg VERSION=1.0", result);
    }

    [Fact]
    public void BuildBuildArgs_MultipleBuildArgs_IncludesAll()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        BuildArgs =
        {
          { "VERSION", "1.0" },
          { "ENV", "production" }
        }
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--build-arg VERSION=1.0", result);
      Assert.Contains("--build-arg ENV=production", result);
    }

    [Fact]
    public void BuildBuildArgs_NoBuildArgs_OmitsBuildArgFlag()
    {
      var config = new ImageBuildConfig { BuildContext = "." };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--build-arg", result);
    }

    #endregion

    #region BuildBuildArgs - Labels

    [Fact]
    public void BuildBuildArgs_SingleLabel_IncludesIt()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        Labels = { { "maintainer", "test@example.com" } }
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--label maintainer=test@example.com", result);
    }

    [Fact]
    public void BuildBuildArgs_MultipleLabels_IncludesAll()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        Labels =
        {
          { "maintainer", "test@example.com" },
          { "version", "3.0" }
        }
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--label maintainer=test@example.com", result);
      Assert.Contains("--label version=3.0", result);
    }

    #endregion

    #region BuildBuildArgs - Target

    [Fact]
    public void BuildBuildArgs_WithTarget_IncludesTargetFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        Target = "runtime"
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--target runtime", result);
    }

    [Fact]
    public void BuildBuildArgs_NullTarget_OmitsTargetFlag()
    {
      var config = new ImageBuildConfig { BuildContext = "." };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--target", result);
    }

    [Fact]
    public void BuildBuildArgs_EmptyTarget_OmitsTargetFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        Target = ""
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--target", result);
    }

    #endregion

    #region BuildBuildArgs - NoCache / Pull / ForceRm

    [Fact]
    public void BuildBuildArgs_NoCache_IncludesFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        NoCache = true
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--no-cache", result);
    }

    [Fact]
    public void BuildBuildArgs_NoCacheFalse_OmitsFlag()
    {
      var config = new ImageBuildConfig { BuildContext = "." };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--no-cache", result);
    }

    [Fact]
    public void BuildBuildArgs_Pull_IncludesFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        Pull = true
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--pull", result);
    }

    [Fact]
    public void BuildBuildArgs_PullFalse_OmitsFlag()
    {
      var config = new ImageBuildConfig { BuildContext = "." };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--pull", result);
    }

    [Fact]
    public void BuildBuildArgs_ForceRm_IncludesFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        ForceRm = true
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--force-rm", result);
    }

    #endregion

    #region BuildBuildArgs - Platform / NetworkMode / Dockerfile / BuildContext

    [Fact]
    public void BuildBuildArgs_Platform_IncludesFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        Platform = "linux/arm64"
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--platform linux/arm64", result);
    }

    [Fact]
    public void BuildBuildArgs_NetworkMode_IncludesFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = ".",
        NetworkMode = "host"
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--network host", result);
    }

    [Fact]
    public void BuildBuildArgs_DockerfileName_IncludesFileFlag()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/src",
        DockerfileName = "Dockerfile.dev"
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.Contains("--file Dockerfile.dev", result);
    }

    [Fact]
    public void BuildBuildArgs_NullBuildContext_DefaultsToDot()
    {
      var config = new ImageBuildConfig { BuildContext = null };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.EndsWith(" .", result);
    }

    [Fact]
    public void BuildBuildArgs_StartsWithBuild()
    {
      var config = new ImageBuildConfig { BuildContext = "." };

      var result = DockerCliImageDriver.BuildBuildArgs(config, null);

      Assert.StartsWith("build ", result);
    }

    [Fact]
    public void BuildBuildArgs_BuildContextIsLastArgument()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/my/project",
        Tags = { "img:v1" },
        NoCache = true
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      Assert.EndsWith(" /my/project", result);
    }

    #endregion

    #region BuildBuildArgs - Combined Scenario

    [Fact]
    public void BuildBuildArgs_FullConfig_ContainsAllFlags()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/app",
        DockerfileName = "Dockerfile.prod",
        Tags = { "myapp:latest", "myapp:v2" },
        BuildArgs = { { "NODE_ENV", "production" }, { "API_URL", "https://api.test" } },
        Labels = { { "team", "backend" } },
        Target = "final",
        NoCache = true,
        Pull = true,
        ForceRm = true,
        Platform = "linux/amd64",
        NetworkMode = "none"
      };

      var result = DockerCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      Assert.StartsWith("build ", result);
      Assert.Contains("--file Dockerfile.prod", result);
      Assert.Contains("--tag myapp:latest", result);
      Assert.Contains("--tag myapp:v2", result);
      Assert.Contains("--build-arg NODE_ENV=production", result);
      Assert.Contains("--label team=backend", result);
      Assert.Contains("--target final", result);
      Assert.Contains("--no-cache", result);
      Assert.Contains("--pull", result);
      Assert.Contains("--force-rm", result);
      Assert.Contains("--platform linux/amd64", result);
      Assert.Contains("--network none", result);
      Assert.Contains("--iidfile", result);
      Assert.EndsWith(" /app", result);
    }

    #endregion

    #region Reflection Helpers

    private static long InvokeParseSize(string sizeStr)
    {
      var method = typeof(DockerCliImageDriver).GetMethod(
        "ParseSize",
        BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (long)method.Invoke(null, [sizeStr]);
    }

    #endregion
  }
}
