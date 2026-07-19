using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Regression tests for the documented fluent builder path: <c>ContainerBuilder.ExecuteAsync</c>
  /// nulls out empty collections before calling the driver, so the Podman container arg builder
  /// must tolerate null collections instead of throwing a <see cref="System.NullReferenceException"/>
  /// on the happy path (P0).
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliContainerNullCollectionTests
  {
    [Fact]
    public void BuildCreateArgs_AllCollectionsNull_DoesNotThrowAndBuildsArgv()
    {
      // Mirror exactly what ContainerBuilder.ExecuteAsync produces for an image-only container:
      // every collection is null, not the empty default the model ships with.
      var config = new ContainerCreateConfig
      {
        Image = "nginx:latest",
        Command = null!,
        Entrypoint = null!,
        Environment = null!,
        PortBindings = null!,
        Volumes = null!,
        Labels = null!,
        ExtraHosts = null!,
        Tmpfs = null!,
        Devices = null!,
        Networks = null!,
        Dns = null!,
        Links = null!,
        CapAdd = null!,
        CapDrop = null!,
        SecurityOpt = null!,
        NetworkAliases = null!
      };

      var result = InvokeBuildCreateArgs("create", config);

      Assert.Equal("create nginx:latest", result);
    }

    [Fact]
    public void BuildCreateArgs_NestedNetworkAliasListNull_DoesNotThrow()
    {
      // A network-alias entry whose value list is null must not NRE on the inner enumeration.
      var config = new ContainerCreateConfig
      {
        Image = "nginx:latest",
        NetworkAliases = new Dictionary<string, List<string>>
        {
          { "frontend", null! }
        }
      };

      var result = InvokeBuildCreateArgs("run", config, detach: true);

      Assert.StartsWith("run -d", result);
      Assert.EndsWith("nginx:latest", result);
    }

    [Fact]
    public void BuildCreateArgs_SomeNullSomeSet_EmitsOnlyPopulatedFlags()
    {
      // Partial-null: the builder can null some collections while populating others.
      var config = new ContainerCreateConfig
      {
        Image = "nginx:latest",
        Environment = new Dictionary<string, string> { { "FOO", "bar" } },
        Volumes = null!,
        CapAdd = null!,
        Networks = null!
      };

      var result = InvokeBuildCreateArgs("create", config);

      Assert.Contains("-e FOO=bar", result);
      Assert.DoesNotContain("-v ", result);
      Assert.DoesNotContain("--cap-add", result);
      Assert.EndsWith("nginx:latest", result);
    }

    private static string InvokeBuildCreateArgs(string command, ContainerCreateConfig config, bool detach = false)
    {
      var method = typeof(PodmanCliContainerDriver).GetMethod(
          "BuildCreateArgs",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (string)method.Invoke(null, [command, config, detach])!;
    }
  }
}
