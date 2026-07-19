using System.Globalization;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// POD-7: numeric CLI arguments built by <see cref="PodmanCliContainerDriver"/> must be
  /// formatted with the invariant culture — a locale with unusual number formatting must not
  /// leak into <c>--stop-timeout</c>, <c>--memory</c>, <c>--cpu-shares</c> and friends.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliContainerArgsCultureTests
  {
    [Fact]
    public void BuildCreateArgs_UnderExoticNegativeSignCulture_EmitsInvariantNumbers()
    {
      // A culture whose negative sign differs from '-' is the only observable divergence for
      // integers (digits are ASCII in all cultures by default).
      var exotic = (CultureInfo)CultureInfo.InvariantCulture.Clone();
      exotic.NumberFormat.NegativeSign = "~";
      var original = CultureInfo.CurrentCulture;
      CultureInfo.CurrentCulture = exotic;
      try
      {
        var args = InvokeBuildCreateArgs("create", new ContainerCreateConfig
        {
          Image = "img",
          StopTimeout = -1
        });

        Assert.Contains("--stop-timeout -1", args);
        Assert.DoesNotContain("~1", args);
      }
      finally
      {
        CultureInfo.CurrentCulture = original;
      }
    }

    [Fact]
    public void BuildCreateArgs_NumericFlags_EmitPlainInvariantDigits()
    {
      var args = InvokeBuildCreateArgs("create", new ContainerCreateConfig
      {
        Image = "img",
        StopTimeout = 30,
        MemoryLimit = 536870912,
        CpuShares = 512,
        CpuQuota = 100000,
        ShmSize = 67108864,
        HealthCheck = new HealthCheckConfig { Test = ["CMD-SHELL", "true"], Retries = 3 }
      });

      Assert.Contains("--stop-timeout 30", args);
      Assert.Contains("--memory 536870912", args);
      Assert.Contains("--cpu-shares 512", args);
      Assert.Contains("--cpu-quota 100000", args);
      Assert.Contains("--shm-size 67108864", args);
      Assert.Contains("--health-retries 3", args);
    }

    [Fact]
    public void BuildListArgs_Limit_EmitsInvariantDigits()
    {
      var args = PodmanCliContainerDriver.BuildListArgs(new ContainerListFilter { Limit = 42 });

      Assert.Contains("--last 42", args);
    }

    /// <summary>
    /// Calls the production private static <c>BuildCreateArgs(string, ContainerCreateConfig, bool)</c>
    /// via reflection (in-repo idiom; reflection does not auto-apply default parameter values).
    /// </summary>
    private static string InvokeBuildCreateArgs(string command, ContainerCreateConfig config)
    {
      var method = typeof(PodmanCliContainerDriver).GetMethod(
          "BuildCreateArgs",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (string)method.Invoke(null, [command, config, false])!;
    }
  }
}
