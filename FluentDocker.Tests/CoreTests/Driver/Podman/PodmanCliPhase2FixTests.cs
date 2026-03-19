using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for Phase 2 Podman CLI fixes:
  /// T2.1 BuildListArgs (Ancestor, Labels filters)
  /// T2.2 BuildImagePruneArgs (filter dict)
  /// T2.3 BuildSystemPruneArgs (Filter dict)
  /// T2.4 BuildStreamEventsArgs (Filters dict)
  /// T2.5 RunAsync detach flag placement
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliPhase2FixTests
  {
    #region T2.1 — BuildListArgs: Ancestor and Labels Filters

    [Fact]
    public void BuildListArgs_NullFilter_ReturnsBaseCommand()
    {
      var result = PodmanCliContainerDriver.BuildListArgs(null);
      Assert.Equal("ps --format json", result);
    }

    [Fact]
    public void BuildListArgs_DefaultFilter_ReturnsBaseCommand()
    {
      var result = PodmanCliContainerDriver.BuildListArgs(new ContainerListFilter());
      Assert.Equal("ps --format json", result);
    }

    [Fact]
    public void BuildListArgs_All_IncludesAllFlag()
    {
      var filter = new ContainerListFilter { All = true };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains(" -a", result);
    }

    [Fact]
    public void BuildListArgs_Name_IncludesNameFilter()
    {
      var filter = new ContainerListFilter { Name = "web" };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter name=web", result);
    }

    [Fact]
    public void BuildListArgs_Status_IncludesStatusFilter()
    {
      var filter = new ContainerListFilter { Status = "running" };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter status=running", result);
    }

    [Fact]
    public void BuildListArgs_Id_IncludesIdFilter()
    {
      var filter = new ContainerListFilter { Id = "abc123" };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter id=abc123", result);
    }

    [Fact]
    public void BuildListArgs_Ancestor_IncludesAncestorFilter()
    {
      var filter = new ContainerListFilter { Ancestor = "nginx:latest" };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter ancestor=nginx:latest", result);
    }

    [Fact]
    public void BuildListArgs_LabelsWithValue_IncludesLabelFilter()
    {
      var filter = new ContainerListFilter
      {
        Labels = new Dictionary<string, string>
        {
          { "app", "web" }
        }
      };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter label=app=web", result);
    }

    [Fact]
    public void BuildListArgs_LabelsWithoutValue_IncludesLabelKeyOnly()
    {
      var filter = new ContainerListFilter
      {
        Labels = new Dictionary<string, string>
        {
          { "managed", "" }
        }
      };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter label=managed", result);
      Assert.DoesNotContain("label=managed=", result);
    }

    [Fact]
    public void BuildListArgs_MultipleLabels_IncludesAllLabelFilters()
    {
      var filter = new ContainerListFilter
      {
        Labels = new Dictionary<string, string>
        {
          { "app", "web" },
          { "env", "prod" }
        }
      };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--filter label=app=web", result);
      Assert.Contains("--filter label=env=prod", result);
    }

    [Fact]
    public void BuildListArgs_Limit_IncludesLastFlag()
    {
      var filter = new ContainerListFilter { Limit = 5 };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.Contains("--last 5", result);
    }

    [Fact]
    public void BuildListArgs_AllFields_ProducesCorrectArgs()
    {
      var filter = new ContainerListFilter
      {
        All = true,
        Name = "web",
        Status = "running",
        Id = "abc",
        Ancestor = "nginx",
        Labels = new Dictionary<string, string> { { "app", "test" } },
        Limit = 10
      };
      var result = PodmanCliContainerDriver.BuildListArgs(filter);
      Assert.StartsWith("ps --format json", result);
      Assert.Contains(" -a", result);
      Assert.Contains("--filter name=web", result);
      Assert.Contains("--filter status=running", result);
      Assert.Contains("--filter id=abc", result);
      Assert.Contains("--filter ancestor=nginx", result);
      Assert.Contains("--filter label=app=test", result);
      Assert.Contains("--last 10", result);
    }

    #endregion

    #region T2.2 — BuildImagePruneArgs: Filter Dict

    [Fact]
    public void BuildImagePruneArgs_NoAll_NoFilter_ReturnsBaseCommand()
    {
      var result = PodmanCliImageDriver.BuildImagePruneArgs(false, null);
      Assert.Equal("image prune -f", result);
    }

    [Fact]
    public void BuildImagePruneArgs_All_IncludesAllFlag()
    {
      var result = PodmanCliImageDriver.BuildImagePruneArgs(true, null);
      Assert.Contains(" -a", result);
    }

    [Fact]
    public void BuildImagePruneArgs_WithFilter_IncludesFilterFlags()
    {
      var filter = new Dictionary<string, string>
      {
        { "until", "24h" },
        { "label", "deprecated" }
      };
      var result = PodmanCliImageDriver.BuildImagePruneArgs(false, filter);
      Assert.Contains("--filter until=24h", result);
      Assert.Contains("--filter label=deprecated", result);
    }

    [Fact]
    public void BuildImagePruneArgs_AllAndFilter_IncludesBoth()
    {
      var filter = new Dictionary<string, string> { { "until", "48h" } };
      var result = PodmanCliImageDriver.BuildImagePruneArgs(true, filter);
      Assert.Contains(" -a", result);
      Assert.Contains("--filter until=48h", result);
    }

    #endregion

    #region T2.3 — BuildSystemPruneArgs: Filter Dict

    [Fact]
    public void BuildSystemPruneArgs_NullConfig_ReturnsBaseCommand()
    {
      var result = PodmanCliSystemDriver.BuildSystemPruneArgs(null);
      Assert.Equal("system prune -f", result);
    }

    [Fact]
    public void BuildSystemPruneArgs_All_IncludesAllFlag()
    {
      var config = new SystemPruneConfig { All = true };
      var result = PodmanCliSystemDriver.BuildSystemPruneArgs(config);
      Assert.Contains(" -a", result);
    }

    [Fact]
    public void BuildSystemPruneArgs_Volumes_IncludesVolumesFlag()
    {
      var config = new SystemPruneConfig { Volumes = true };
      var result = PodmanCliSystemDriver.BuildSystemPruneArgs(config);
      Assert.Contains("--volumes", result);
    }

    [Fact]
    public void BuildSystemPruneArgs_WithFilter_IncludesFilterFlags()
    {
      var config = new SystemPruneConfig
      {
        Filter = new Dictionary<string, string>
        {
          { "until", "24h" },
          { "label", "temp" }
        }
      };
      var result = PodmanCliSystemDriver.BuildSystemPruneArgs(config);
      Assert.Contains("--filter until=24h", result);
      Assert.Contains("--filter label=temp", result);
    }

    [Fact]
    public void BuildSystemPruneArgs_AllFieldsSet_IncludesAll()
    {
      var config = new SystemPruneConfig
      {
        All = true,
        Volumes = true,
        Filter = new Dictionary<string, string> { { "label", "removable" } }
      };
      var result = PodmanCliSystemDriver.BuildSystemPruneArgs(config);
      Assert.Contains(" -a", result);
      Assert.Contains("--volumes", result);
      Assert.Contains("--filter label=removable", result);
    }

    [Fact]
    public void BuildSystemPruneArgs_EmptyFilter_NoFilterFlags()
    {
      var config = new SystemPruneConfig
      {
        Filter = new Dictionary<string, string>()
      };
      var result = PodmanCliSystemDriver.BuildSystemPruneArgs(config);
      Assert.DoesNotContain("--filter", result);
    }

    #endregion

    #region T2.4 — BuildStreamEventsArgs: Filters Dict

    [Fact]
    public void BuildStreamEventsArgs_NullConfig_ReturnsBaseCommand()
    {
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(null);
      Assert.Equal("events --format json", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_SinceUntil_IncludesTimeFlags()
    {
      var config = new StreamEventsConfig
      {
        Since = "2024-01-01",
        Until = "2024-12-31"
      };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);
      Assert.Contains("--since 2024-01-01", result);
      Assert.Contains("--until 2024-12-31", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_Types_IncludesTypeFilters()
    {
      var config = new StreamEventsConfig
      {
        Types = new List<string> { "container", "image" }
      };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);
      Assert.Contains("--filter type=container", result);
      Assert.Contains("--filter type=image", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_Actions_IncludesEventFilters()
    {
      var config = new StreamEventsConfig
      {
        Actions = new List<string> { "start", "stop" }
      };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);
      Assert.Contains("--filter event=start", result);
      Assert.Contains("--filter event=stop", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_GenericFilters_IncludesFilterFlags()
    {
      var config = new StreamEventsConfig
      {
        Filters = new Dictionary<string, string>
        {
          { "container", "web1" },
          { "label", "env=prod" }
        }
      };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);
      Assert.Contains("--filter container=web1", result);
      Assert.Contains("--filter label=env=prod", result);
    }

    [Fact]
    public void BuildStreamEventsArgs_AllFields_ProducesCorrectArgs()
    {
      var config = new StreamEventsConfig
      {
        Since = "1h",
        Until = "now",
        Types = new List<string> { "container" },
        Actions = new List<string> { "die" },
        Filters = new Dictionary<string, string> { { "name", "web" } }
      };
      var result = PodmanCliStreamDriver.BuildStreamEventsArgs(config);
      Assert.Contains("--since 1h", result);
      Assert.Contains("--until now", result);
      Assert.Contains("--filter type=container", result);
      Assert.Contains("--filter event=die", result);
      Assert.Contains("--filter name=web", result);
    }

    #endregion

    #region T2.5 — RunAsync Detach Flag Placement

    [Fact]
    public void RunDetach_WithName_DetachFlagAfterRun()
    {
      var config = new ContainerCreateConfig
      {
        Image = "nginx",
        Name = "web",
        Detach = true
      };
      var args = InvokeBuildCreateArgs("run", config, detach: true);

      Assert.StartsWith("run -d", args);
      Assert.Contains("--name web", args);
      Assert.Contains("nginx", args);
    }

    [Fact]
    public void RunDetach_MinimalConfig_DetachFlagAfterRun()
    {
      var config = new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true
      };
      var args = InvokeBuildCreateArgs("run", config, detach: true);

      Assert.StartsWith("run -d", args);
      Assert.EndsWith("nginx", args);
    }

    [Fact]
    public void RunDetach_WithManyFlags_DetachFlagAfterRun()
    {
      var config = new ContainerCreateConfig
      {
        Image = "nginx",
        Name = "web",
        Hostname = "myhost",
        Privileged = true,
        AutoRemove = true,
        Detach = true,
        Environment = new Dictionary<string, string> { { "FOO", "bar" } }
      };
      var args = InvokeBuildCreateArgs("run", config, detach: true);

      Assert.StartsWith("run -d", args);
      Assert.Contains("--name web", args);
      Assert.Contains("--hostname myhost", args);
      Assert.Contains("--privileged", args);
      Assert.Contains("--rm", args);
      Assert.Contains("-e FOO=bar", args);
      Assert.EndsWith("nginx", args);
    }

    [Fact]
    public void RunNoDetach_NoDetachFlag()
    {
      var config = new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = false
      };
      var args = InvokeBuildCreateArgs("run", config, detach: false);

      Assert.StartsWith("run", args);
      Assert.DoesNotContain("-d", args);
    }

    #endregion

    #region Helpers

    private static string InvokeBuildCreateArgs(string command, ContainerCreateConfig config, bool detach = false)
    {
      var method = typeof(PodmanCliContainerDriver).GetMethod(
          "BuildCreateArgs",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { command, config, detach });
    }

    #endregion
  }
}
