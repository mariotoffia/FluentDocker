using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public class PodmanCliImageBuildArgsTests
  {
    [Fact]
    public void BuildBuildArgs_MinimalConfig_ContainsIidFile()
    {
      var config = new ImageBuildConfig { BuildContext = "/src" };
      var result = PodmanCliImageDriver.BuildBuildArgs(config, "/tmp/iid.txt");

      Assert.Contains("--iidfile \"/tmp/iid.txt\"", result);
      Assert.StartsWith("build ", result);
      Assert.EndsWith(" /src", result);
    }

    [Fact]
    public void BuildBuildArgs_NullIidPath_OmitsIidFile()
    {
      var config = new ImageBuildConfig { BuildContext = "." };
      var result = PodmanCliImageDriver.BuildBuildArgs(config, null);

      Assert.DoesNotContain("--iidfile", result);
    }

    [Fact]
    public void BuildBuildArgs_EmptyIidPath_OmitsIidFile()
    {
      var config = new ImageBuildConfig { BuildContext = "." };
      var result = PodmanCliImageDriver.BuildBuildArgs(config, "");

      Assert.DoesNotContain("--iidfile", result);
    }

    [Fact]
    public void BuildBuildArgs_WithTags_IncludesTagsAndIidFile()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/app",
        Tags = { "myimage:latest", "myimage:v1.0" }
      };

      var result = PodmanCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      Assert.Contains("-t myimage:latest", result);
      Assert.Contains("-t myimage:v1.0", result);
      Assert.Contains("--iidfile \"/tmp/iid\"", result);
    }

    [Fact]
    public void BuildBuildArgs_AllOptions_ProducesCorrectArgs()
    {
      var config = new ImageBuildConfig
      {
        BuildContext = "/ctx",
        DockerfileName = "Containerfile",
        Tags = { "app:v2" },
        BuildArgs = { { "ENV", "prod" } },
        Labels = { { "version", "2.0" } },
        Target = "runtime",
        NoCache = true,
        Pull = true,
        Rm = true,
        ForceRm = true,
        Squash = true,
        Platform = "linux/arm64",
        NetworkMode = "host"
      };

      var result = PodmanCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      Assert.Contains("-f Containerfile", result);
      Assert.Contains("-t app:v2", result);
      Assert.Contains("--build-arg ENV=prod", result);
      Assert.Contains("--label version=2.0", result);
      Assert.Contains("--target runtime", result);
      Assert.Contains("--no-cache", result);
      Assert.Contains("--pull", result);
      Assert.Contains("--rm", result);
      Assert.Contains("--force-rm", result);
      Assert.Contains("--squash", result);
      Assert.Contains("--platform linux/arm64", result);
      Assert.Contains("--network host", result);
      Assert.Contains("--iidfile \"/tmp/iid\"", result);
      Assert.EndsWith(" /ctx", result);
    }

    [Fact]
    public void BuildBuildArgs_NullBuildContext_DefaultsToDot()
    {
      var config = new ImageBuildConfig { BuildContext = null };
      var result = PodmanCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      Assert.EndsWith(" .", result);
    }

    [Fact]
    public void BuildBuildArgs_IidFileBeforeBuildContext()
    {
      var config = new ImageBuildConfig { BuildContext = "/src" };
      var result = PodmanCliImageDriver.BuildBuildArgs(config, "/tmp/iid");

      var iidPos = result.IndexOf("--iidfile");
      var ctxPos = result.LastIndexOf("/src");
      Assert.True(iidPos < ctxPos, "--iidfile should appear before the build context");
    }
  }
}
