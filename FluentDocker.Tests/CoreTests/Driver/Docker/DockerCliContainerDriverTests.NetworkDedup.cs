using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// DC-9: NetworkMode and Networks may overlap; the real arg builder must emit exactly one
  /// <c>--network</c> flag per distinct network (union of NetworkMode + Networks, order
  /// preserved, NetworkMode first). These tests invoke the production
  /// <c>DockerCliContainerDriver.BuildCreateArgs</c> via reflection (the in-repo idiom for
  /// private static builders).
  /// </summary>
  public partial class DockerCliContainerDriverTests
  {
    #region --network De-Duplication (DC-9)

    [Fact]
    public void RealCreateArgs_NetworkModeAlsoInNetworks_EmitsNetworkOnce()
    {
      var args = InvokeRealBuildCreateArgs("create", new ContainerCreateConfig
      {
        Image = "nginx",
        NetworkMode = "backend",
        Networks = ["backend"]
      });

      Assert.Equal(1, args.Count(a => a == "--network backend"));
    }

    [Fact]
    public void RealCreateArgs_NetworkModeAndDifferentNetworks_EmitsModeFirstThenRemainder()
    {
      var args = InvokeRealBuildCreateArgs("create", new ContainerCreateConfig
      {
        Image = "nginx",
        NetworkMode = "custom",
        Networks = ["frontend", "custom", "backend"]
      });

      var networkArgs = args.Where(a => a.StartsWith("--network ", System.StringComparison.Ordinal)).ToList();
      Assert.Equal(["--network custom", "--network frontend", "--network backend"], networkArgs);
    }

    [Fact]
    public void RealCreateArgs_DuplicateNetworksEntries_AreDeduplicated()
    {
      var args = InvokeRealBuildCreateArgs("create", new ContainerCreateConfig
      {
        Image = "nginx",
        Networks = ["frontend", "frontend", "backend"]
      });

      var networkArgs = args.Where(a => a.StartsWith("--network ", System.StringComparison.Ordinal)).ToList();
      Assert.Equal(["--network frontend", "--network backend"], networkArgs);
    }

    [Fact]
    public void RealCreateArgs_NetworksWithoutNetworkMode_AllEmitted()
    {
      var args = InvokeRealBuildCreateArgs("create", new ContainerCreateConfig
      {
        Image = "nginx",
        Networks = ["frontend", "backend"]
      });

      Assert.Contains("--network frontend", args);
      Assert.Contains("--network backend", args);
      Assert.Equal(2, args.Count(a => a.StartsWith("--network ", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void RealCreateArgs_NetworkModeOnly_EmitsSingleNetworkFlag()
    {
      var args = InvokeRealBuildCreateArgs("create", new ContainerCreateConfig
      {
        Image = "nginx",
        NetworkMode = "host"
      });

      Assert.Equal(["--network host"],
          args.Where(a => a.StartsWith("--network ", System.StringComparison.Ordinal)).ToList());
    }

    /// <summary>
    /// Calls the production <c>BuildCreateArgs(string, ContainerCreateConfig, bool, string)</c>
    /// via reflection. Reflection does not auto-apply default values, so the optional
    /// <c>detach</c>/<c>cidFile</c> parameters are passed explicitly.
    /// </summary>
    private static List<string> InvokeRealBuildCreateArgs(string command, ContainerCreateConfig config)
    {
      var method = typeof(DockerCliContainerDriver).GetMethod(
          "BuildCreateArgs",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (List<string>)method.Invoke(null, [command, config, false, null])!;
    }

    #endregion
  }
}
