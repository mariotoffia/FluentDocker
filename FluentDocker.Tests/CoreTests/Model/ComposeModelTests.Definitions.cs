using FluentDocker.Model.Compose;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class ComposeModelTests
  {
    #region DeployDefinition Tests

    [Fact]
    public void DeployDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var deploy = new DeployDefinition();

      Assert.Equal("vip", deploy.EndpointMode);
      Assert.Equal("replicated", deploy.Mode);
      Assert.Equal(1, deploy.Replicas);
      Assert.NotNull(deploy.Labels);
      Assert.Empty(deploy.Labels);
      Assert.Null(deploy.Placement);
      Assert.Null(deploy.Resources);
      Assert.Null(deploy.RestartPolicy);
      Assert.Null(deploy.RollbackConfig);
      Assert.Null(deploy.UpdateConfig);
    }

    [Fact]
    public void DeployDefinition_SetProperties_RoundTrips()
    {
      var deploy = new DeployDefinition
      {
        EndpointMode = "dnsrr",
        Mode = "global",
        Replicas = 5,
        Placement = new PlacementDefinition(),
        Resources = new ResourcesDefinition(),
        RestartPolicy = new RestartPolicyDefinition(),
        RollbackConfig = new DeployConfigDefinition(),
        UpdateConfig = new DeployConfigDefinition()
      };
      deploy.Labels["com.example.description"] = "Web service";

      Assert.Equal("dnsrr", deploy.EndpointMode);
      Assert.Equal("global", deploy.Mode);
      Assert.Equal(5, deploy.Replicas);
      Assert.NotNull(deploy.Placement);
      Assert.NotNull(deploy.Resources);
      Assert.NotNull(deploy.RestartPolicy);
      Assert.NotNull(deploy.RollbackConfig);
      Assert.NotNull(deploy.UpdateConfig);
      Assert.Single(deploy.Labels);
    }

    #endregion

    #region DeployConfigDefinition Tests

    [Fact]
    public void DeployConfigDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var cfg = new DeployConfigDefinition();

      Assert.Equal(0, cfg.Parallelism);
      Assert.Null(cfg.Delay);
      Assert.Equal("pause", cfg.FailureAction);
      Assert.Equal("0s", cfg.Monitor);
      Assert.Equal(0, cfg.MaxFailureRatio);
      Assert.Equal("stop-first", cfg.Order);
    }

    [Fact]
    public void DeployConfigDefinition_SetProperties_RoundTrips()
    {
      var cfg = new DeployConfigDefinition
      {
        Parallelism = 3,
        Delay = "10s",
        FailureAction = "continue",
        Monitor = "60s",
        MaxFailureRatio = 2,
        Order = "start-first"
      };

      Assert.Equal(3, cfg.Parallelism);
      Assert.Equal("10s", cfg.Delay);
      Assert.Equal("continue", cfg.FailureAction);
      Assert.Equal("60s", cfg.Monitor);
      Assert.Equal(2, cfg.MaxFailureRatio);
      Assert.Equal("start-first", cfg.Order);
    }

    #endregion

    #region HealthCheckDefinition Tests

    [Fact]
    public void HealthCheckDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var hc = new HealthCheckDefinition();

      Assert.True(hc.Enabled);
      Assert.NotNull(hc.Test);
      Assert.Empty(hc.Test);
      Assert.Equal("30s", hc.Interval);
      Assert.Equal("30s", hc.Timeout);
      Assert.Equal(3, hc.Retries);
      Assert.Equal("0s", hc.StartPeriod);
    }

    [Fact]
    public void HealthCheckDefinition_SetProperties_RoundTrips()
    {
      var hc = new HealthCheckDefinition
      {
        Enabled = false,
        Interval = "1m30s",
        Timeout = "10s",
        Retries = 5,
        StartPeriod = "40s"
      };
      hc.Test.Add("CMD");
      hc.Test.Add("curl");
      hc.Test.Add("-f");
      hc.Test.Add("http://localhost");

      Assert.False(hc.Enabled);
      Assert.Equal(4, hc.Test.Count);
      Assert.Equal("CMD", hc.Test[0]);
      Assert.Equal("1m30s", hc.Interval);
      Assert.Equal("10s", hc.Timeout);
      Assert.Equal(5, hc.Retries);
      Assert.Equal("40s", hc.StartPeriod);
    }

    [Fact]
    public void HealthCheckDefinition_Disabled_OverridesTest()
    {
      var hc = new HealthCheckDefinition { Enabled = false };
      hc.Test.Add("NONE");

      Assert.False(hc.Enabled);
      Assert.Single(hc.Test);
      Assert.Equal("NONE", hc.Test[0]);
    }

    #endregion

    #region LoggingDefinition Tests

    [Fact]
    public void LoggingDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var log = new LoggingDefinition();

      Assert.Equal("json-file", log.Driver);
      Assert.NotNull(log.Options);
      Assert.Empty(log.Options);
    }

    [Fact]
    public void LoggingDefinition_SetProperties_RoundTrips()
    {
      var log = new LoggingDefinition { Driver = "syslog" };
      log.Options["syslog-address"] = "tcp://192.168.0.42:123";

      Assert.Equal("syslog", log.Driver);
      Assert.Single(log.Options);
      Assert.Equal(
        "tcp://192.168.0.42:123", log.Options["syslog-address"]);
    }

    #endregion

    #region RestartPolicyDefinition Tests

    [Fact]
    public void RestartPolicyDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var rp = new RestartPolicyDefinition();

      Assert.Equal("any", rp.Condition);
      Assert.Equal("0", rp.Delay);
      Assert.Equal(int.MaxValue, rp.MaxAttempts);
      Assert.Null(rp.Window);
    }

    [Fact]
    public void RestartPolicyDefinition_SetProperties_RoundTrips()
    {
      var rp = new RestartPolicyDefinition
      {
        Condition = "on-failure",
        Delay = "5s",
        MaxAttempts = 3,
        Window = "120s"
      };

      Assert.Equal("on-failure", rp.Condition);
      Assert.Equal("5s", rp.Delay);
      Assert.Equal(3, rp.MaxAttempts);
      Assert.Equal("120s", rp.Window);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("on-failure")]
    [InlineData("any")]
    public void RestartPolicyDefinition_ConditionValues_AreAccepted(
      string condition)
    {
      var rp = new RestartPolicyDefinition { Condition = condition };
      Assert.Equal(condition, rp.Condition);
    }

    #endregion

    #region PlacementDefinition Tests

    [Fact]
    public void PlacementDefinition_DefaultConstruction_HasEmptyCollections()
    {
      var placement = new PlacementDefinition();

      Assert.NotNull(placement.Constraints);
      Assert.Empty(placement.Constraints);
      Assert.NotNull(placement.Preferences);
      Assert.Empty(placement.Preferences);
    }

    [Fact]
    public void PlacementDefinition_SetProperties_RoundTrips()
    {
      var placement = new PlacementDefinition();
      placement.Constraints.Add("node.role==manager");
      placement.Constraints.Add(
        "engine.labels.operatingsystem==ubuntu 14.04");
      placement.Preferences["spread"] = "node.labels.zone";

      Assert.Equal(2, placement.Constraints.Count);
      Assert.Contains("node.role==manager", placement.Constraints);
      Assert.Single(placement.Preferences);
      Assert.Equal(
        "node.labels.zone", placement.Preferences["spread"]);
    }

    #endregion

    #region ResourcesDefinition / ResourcesItemDefinition Tests

    [Fact]
    public void ResourcesDefinition_DefaultConstruction_HasNullValues()
    {
      var res = new ResourcesDefinition();

      Assert.Null(res.Limits);
      Assert.Null(res.Reservations);
    }

    [Fact]
    public void ResourcesDefinition_SetProperties_RoundTrips()
    {
      var res = new ResourcesDefinition
      {
        Limits = new ResourcesItemDefinition
        {
          Cpus = "0.50",
          Memory = "50M"
        },
        Reservations = new ResourcesItemDefinition
        {
          Cpus = "0.25",
          Memory = "20M"
        }
      };

      Assert.Equal("0.50", res.Limits.Cpus);
      Assert.Equal("50M", res.Limits.Memory);
      Assert.Equal("0.25", res.Reservations.Cpus);
      Assert.Equal("20M", res.Reservations.Memory);
    }

    [Fact]
    public void ResourcesItemDefinition_DefaultConstruction_HasNullProperties()
    {
      var item = new ResourcesItemDefinition();

      Assert.Null(item.Cpus);
      Assert.Null(item.Memory);
    }

    #endregion

    #region TmpFsDefinition Tests

    [Fact]
    public void TmpFsDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var tmpfs = new TmpFsDefinition();

      Assert.Equal("tmpfs", tmpfs.Type);
      Assert.Null(tmpfs.Target);
      Assert.Equal(-1, tmpfs.Size);
    }

    [Fact]
    public void TmpFsDefinition_SetProperties_RoundTrips()
    {
      var tmpfs = new TmpFsDefinition
      {
        Type = "tmpfs",
        Target = "/run",
        Size = 1048576
      };

      Assert.Equal("tmpfs", tmpfs.Type);
      Assert.Equal("/run", tmpfs.Target);
      Assert.Equal(1048576, tmpfs.Size);
    }

    #endregion

    #region UlimitDefinition Tests

    [Fact]
    public void UlimitDefinition_DefaultConstruction_HasZeroValues()
    {
      var ulimit = new UlimitDefinition();

      Assert.Equal(0, ulimit.MappingSoft);
      Assert.Equal(0, ulimit.MappingHard);
    }

    [Fact]
    public void UlimitDefinition_SetProperties_RoundTrips()
    {
      var ulimit = new UlimitDefinition
      {
        MappingSoft = 65535,
        MappingHard = 65535
      };

      Assert.Equal(65535, ulimit.MappingSoft);
      Assert.Equal(65535, ulimit.MappingHard);
    }

    [Fact]
    public void UlimitDefinition_DifferentSoftAndHard_RoundTrips()
    {
      var ulimit = new UlimitDefinition
      {
        MappingSoft = 1024,
        MappingHard = 65535
      };

      Assert.Equal(1024, ulimit.MappingSoft);
      Assert.Equal(65535, ulimit.MappingHard);
      Assert.NotEqual(ulimit.MappingSoft, ulimit.MappingHard);
    }

    #endregion
  }
}
