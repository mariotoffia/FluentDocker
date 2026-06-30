using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests that dynamic values flowing into <c>podman build</c> / <c>podman image prune</c>
  /// argument strings are quoted through the shared quoting helper (FIX-6), so a value
  /// containing whitespace or shell metacharacters cannot break out of its argument slot.
  /// Exercised through the public static arg-builders.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliImageArgQuotingTests
  {
    [Fact]
    public void BuildBuildArgs_TagWithSpace_IsQuoted()
    {
      var config = new ImageBuildConfig
      {
        Tags = ["my image:latest"],
        BuildContext = "."
      };

      var args = PodmanCliImageDriver.BuildBuildArgs(config, null!);

      Assert.Contains("-t \"my image:latest\"", args);
    }

    [Fact]
    public void BuildBuildArgs_SafeTag_IsNotQuoted()
    {
      var config = new ImageBuildConfig
      {
        Tags = ["nginx:latest"],
        BuildContext = "."
      };

      var args = PodmanCliImageDriver.BuildBuildArgs(config, null!);

      Assert.Contains("-t nginx:latest", args);
      Assert.DoesNotContain("\"nginx:latest\"", args);
    }

    [Fact]
    public void BuildBuildArgs_BuildContextWithSpace_IsQuoted()
    {
      var config = new ImageBuildConfig
      {
        Tags = ["app:1"],
        BuildContext = "/path/with space"
      };

      var args = PodmanCliImageDriver.BuildBuildArgs(config, null!);

      Assert.Contains("\"/path/with space\"", args);
    }

    [Fact]
    public void BuildBuildArgs_BuildArgWithSpace_IsQuoted()
    {
      var config = new ImageBuildConfig
      {
        Tags = ["app:1"],
        BuildContext = ".",
        BuildArgs = new Dictionary<string, string> { ["MSG"] = "hello world" }
      };

      var args = PodmanCliImageDriver.BuildBuildArgs(config, null!);

      Assert.Contains("--build-arg \"MSG=hello world\"", args);
    }

    [Fact]
    public void BuildBuildArgs_IidFileWithSpace_IsQuoted()
    {
      var config = new ImageBuildConfig
      {
        Tags = ["app:1"],
        BuildContext = "."
      };

      var args = PodmanCliImageDriver.BuildBuildArgs(config, "/tmp dir/iid.txt");

      Assert.Contains("--iidfile \"/tmp dir/iid.txt\"", args);
    }

    [Fact]
    public void BuildImagePruneArgs_FilterValueWithSpace_IsQuoted()
    {
      var args = PodmanCliImageDriver.BuildImagePruneArgs(
          all: true,
          filter: new Dictionary<string, string> { ["label"] = "team=core team" });

      Assert.Contains("--filter \"label=team=core team\"", args);
    }
  }
}
