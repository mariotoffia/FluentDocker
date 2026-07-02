using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for consistent CLI argument quoting (P5): dynamic values that contain a space or
  /// other shell metacharacter must be wrapped by <c>QuoteArgumentIfNeeded</c> so they survive as a
  /// single argv token instead of splitting into an invalid command line.
  /// </summary>
  public partial class PodmanCliStreamDriverTests
  {
    #region Argument Quoting (P5)

    [Fact]
    public void BuildStreamLogsArgs_SinceWithSpace_IsQuoted()
    {
      var config = new StreamLogsConfig { Follow = false, Since = "2024-01-01 12:00:00" };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--since \"2024-01-01 12:00:00\"", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_UntilWithSpace_IsQuoted()
    {
      var config = new StreamLogsConfig { Follow = false, Until = "2024-01-01 12:00:00" };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("ctr1", config);

      Assert.Contains("--until \"2024-01-01 12:00:00\"", result);
    }

    [Fact]
    public void BuildStreamLogsArgs_ContainerIdWithSpace_IsQuoted()
    {
      var config = new StreamLogsConfig { Follow = false };
      var result = PodmanCliStreamDriver.BuildStreamLogsArgs("my container", config);

      Assert.EndsWith("\"my container\"", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_FilterValueWithSpace_IsQuoted()
    {
      var config = new StreamEventsConfig
      {
        Filters = new Dictionary<string, string> { { "name", "my app" } }
      };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--filter \"name=my app\"", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_SinceWithSpace_IsQuoted()
    {
      var config = new StreamEventsConfig { Since = "2024-01-01 00:00:00" };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);

      Assert.Contains("--since \"2024-01-01 00:00:00\"", result);
    }

    [Fact]
    public void BuildGlobalArgs_HostWithSpace_IsQuoted()
    {
      var context = new DriverContext("podman", "unix:///path with space.sock");
      var result = PodmanCliDriverBase.BuildGlobalArgs(context);

      Assert.Equal("--url \"unix:///path with space.sock\"", result);
    }

    [Fact]
    public void BuildGlobalArgs_HostWithoutMetachars_IsNotQuoted()
    {
      var context = new DriverContext("podman", "unix:///run/podman.sock");
      var result = PodmanCliDriverBase.BuildGlobalArgs(context);

      Assert.Equal("--url unix:///run/podman.sock", result);
    }

    #endregion
  }
}
