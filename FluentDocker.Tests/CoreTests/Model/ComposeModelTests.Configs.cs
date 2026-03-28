using System.Collections.Generic;
using System.Linq;
using FluentDocker.Model.Compose;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class ComposeModelTests
  {
    #region ConfigurationDefinition Tests

    [Fact]
    public void ConfigurationDefinition_DefaultConstruction_HasEmptyItems()
    {
      var cfg = new ConfigurationDefinition();

      Assert.NotNull(cfg.Items);
      Assert.Empty(cfg.Items);
    }

    [Fact]
    public void ConfigurationDefinition_AddItems_RoundTrips()
    {
      var cfg = new ConfigurationDefinition();
      cfg.Items["my_config"] = new ConfigurationItemDefinition
      {
        Name = "my_config"
      };
      cfg.Items["my_config"].NameValues["key1"] = "value1";

      Assert.Single(cfg.Items);
      Assert.Equal("my_config", cfg.Items["my_config"].Name);
      Assert.Equal(
        "value1", cfg.Items["my_config"].NameValues["key1"]);
    }

    #endregion

    #region ConfigurationItemDefinition Tests

    [Fact]
    public void ConfigurationItemDefinition_DefaultConstruction_HasEmptyNameValues()
    {
      var item = new ConfigurationItemDefinition();

      Assert.Null(item.Name);
      Assert.NotNull(item.NameValues);
      Assert.Empty(item.NameValues);
    }

    #endregion

    #region ConfigLongDefinition Tests

    [Fact]
    public void ConfigLongDefinition_DefaultConstruction_HasNullProperties()
    {
      var cfg = new ConfigLongDefinition();

      Assert.Null(cfg.Source);
      Assert.Null(cfg.Target);
      Assert.Null(cfg.Uid);
      Assert.Null(cfg.Gid);
      Assert.Null(cfg.Mode);
    }

    [Fact]
    public void ConfigLongDefinition_SetProperties_RoundTrips()
    {
      var cfg = new ConfigLongDefinition
      {
        Source = "my_config",
        Target = "/redis_config",
        Uid = "103",
        Gid = "103",
        Mode = "0440"
      };

      Assert.Equal("my_config", cfg.Source);
      Assert.Equal("/redis_config", cfg.Target);
      Assert.Equal("103", cfg.Uid);
      Assert.Equal("103", cfg.Gid);
      Assert.Equal("0440", cfg.Mode);
    }

    #endregion

    #region ServiceNetworkDefinition Tests

    [Fact]
    public void ServiceNetworkDefinition_DefaultConstruction_HasEmptyAliases()
    {
      var net = new ServiceNetworkDefinition();

      Assert.Null(net.Name);
      Assert.Null(net.IpV4Address);
      Assert.Null(net.IpV6Address);
      Assert.NotNull(net.Aliases);
      Assert.Empty(net.Aliases);
    }

    [Fact]
    public void ServiceNetworkDefinition_SetProperties_RoundTrips()
    {
      var net = new ServiceNetworkDefinition
      {
        Name = "app_net",
        IpV4Address = "172.16.238.10",
        IpV6Address = "2001:3984:3989::10"
      };
      net.Aliases.Add("alias1");
      net.Aliases.Add("alias2");

      Assert.Equal("app_net", net.Name);
      Assert.Equal("172.16.238.10", net.IpV4Address);
      Assert.Equal("2001:3984:3989::10", net.IpV6Address);
      Assert.Equal(2, net.Aliases.Count);
      Assert.Contains("alias1", net.Aliases);
      Assert.Contains("alias2", net.Aliases);
    }

    #endregion

    #region ShortSecret Tests

    [Fact]
    public void ShortSecret_DefaultConstruction_HasNullProperties()
    {
      var secret = new ShortSecret();

      Assert.Null(secret.Name);
      Assert.Null(secret.FilePath);
      Assert.False(secret.IsExternal);
    }

    [Fact]
    public void ShortSecret_SetProperties_RoundTrips()
    {
      var secret = new ShortSecret
      {
        Name = "db_password",
        FilePath = "./db_password.txt",
        IsExternal = false
      };

      Assert.Equal("db_password", secret.Name);
      Assert.Equal("./db_password.txt", secret.FilePath);
      Assert.False(secret.IsExternal);
    }

    [Fact]
    public void ShortSecret_External_DiscardsFilePath()
    {
      var secret = new ShortSecret
      {
        Name = "db_password",
        IsExternal = true
      };

      Assert.True(secret.IsExternal);
      Assert.Null(secret.FilePath);
    }

    [Fact]
    public void ShortSecret_ImplementsISecret()
    {
      ISecret secret = new ShortSecret { Name = "test" };
      Assert.IsType<ShortSecret>(secret);
    }

    #endregion

    #region LongSecret Tests

    [Fact]
    public void LongSecret_DefaultConstruction_HasExpectedDefaults()
    {
      var secret = new LongSecret();

      Assert.Null(secret.Source);
      Assert.Null(secret.Target);
      Assert.Equal(0, secret.Uid);
      Assert.Equal(0, secret.Gid);
      Assert.Equal("0444", secret.Mode);
    }

    [Fact]
    public void LongSecret_SetProperties_RoundTrips()
    {
      var secret = new LongSecret
      {
        Source = "my_secret",
        Target = "redis_secret",
        Uid = 103,
        Gid = 103,
        Mode = "0440"
      };

      Assert.Equal("my_secret", secret.Source);
      Assert.Equal("redis_secret", secret.Target);
      Assert.Equal(103, secret.Uid);
      Assert.Equal(103, secret.Gid);
      Assert.Equal("0440", secret.Mode);
    }

    [Fact]
    public void LongSecret_ImplementsISecret()
    {
      ISecret secret = new LongSecret { Source = "test" };
      Assert.IsType<LongSecret>(secret);
    }

    #endregion

    #region Interface Polymorphism Tests

    [Fact]
    public void ISecret_CanMixShortAndLongInCollection()
    {
      var secrets = new List<ISecret>
      {
        new ShortSecret { Name = "short_secret" },
        new LongSecret
        {
          Source = "long_secret", Target = "secret_file"
        }
      };

      Assert.Equal(2, secrets.Count);

      var shortSecret = Assert.IsType<ShortSecret>(secrets[0]);
      Assert.Equal("short_secret", shortSecret.Name);

      var longSecret = Assert.IsType<LongSecret>(secrets[1]);
      Assert.Equal("long_secret", longSecret.Source);
      Assert.Equal("secret_file", longSecret.Target);
    }

    [Fact]
    public void IServiceVolumeDefinition_CanMixShortAndLongInCollection()
    {
      var volumes = new List<IServiceVolumeDefinition>
      {
        new ShortServiceVolumeDefinition { Entry = "/var/lib/mysql" },
        new LongServiceVolumeDefinition
        {
          Source = "mydata",
          Target = "/data",
          Type = VolumeType.Volume
        }
      };

      Assert.Equal(2, volumes.Count);

      var shortVol = Assert.IsType<ShortServiceVolumeDefinition>(
        volumes[0]);
      Assert.Equal("/var/lib/mysql", shortVol.Entry);

      var longVol = Assert.IsType<LongServiceVolumeDefinition>(
        volumes[1]);
      Assert.Equal("mydata", longVol.Source);
    }

    [Fact]
    public void IPortsDefinition_CanMixShortAndLongInCollection()
    {
      var ports = new List<IPortsDefinition>
      {
        new PortsShortDefinition { Entry = "8080:80" },
        new PortsLongDefinition { Target = 80, Published = 8080 }
      };

      Assert.Equal(2, ports.Count);

      var shortPort = Assert.IsType<PortsShortDefinition>(ports[0]);
      Assert.Equal("8080:80", shortPort.Entry);

      var longPort = Assert.IsType<PortsLongDefinition>(ports[1]);
      Assert.Equal(80, longPort.Target);
      Assert.Equal(8080, longPort.Published);
    }

    #endregion

    #region Full Service Composition Test

    [Fact]
    public void ComposeServiceDefinition_FullComposition_AllSubObjectsWork()
    {
      var svc = new ComposeServiceDefinition
      {
        Name = "redis",
        Image = "redis:latest",
        Build = new BuildDefinition
        {
          Context = ".",
          Dockerfile = "Dockerfile",
          Target = "prod"
        },
        HealthCheck = new HealthCheckDefinition
        {
          Interval = "10s",
          Timeout = "5s",
          Retries = 3
        },
        Logging = new LoggingDefinition { Driver = "syslog" }
      };

      svc.DependsOn.Add("db");
      svc.Environment["REDIS_PASSWORD"] = "secret";
      svc.Ports.Add(
        new PortsShortDefinition { Entry = "6379:6379" });
      svc.Volumes.Add(new ShortServiceVolumeDefinition
      {
        Entry = "./data:/data"
      });
      svc.Networks.Add(new ServiceNetworkDefinition
      {
        Name = "backend",
        IpV4Address = "172.16.0.10"
      });
      svc.TmpFs.Add(new TmpFsDefinition
      {
        Target = "/tmp",
        Size = 1048576
      });
      svc.ConfigLong.Add(new ConfigLongDefinition
      {
        Source = "redis_config",
        Target = "/etc/redis.conf",
        Mode = "0444"
      });

      Assert.Equal("redis", svc.Name);
      Assert.Equal("redis:latest", svc.Image);
      Assert.NotNull(svc.Build);
      Assert.Equal("prod", svc.Build.Target);
      Assert.NotNull(svc.HealthCheck);
      Assert.Equal(3, svc.HealthCheck.Retries);
      Assert.NotNull(svc.Logging);
      Assert.Equal("syslog", svc.Logging.Driver);
      Assert.Single(svc.DependsOn);
      Assert.Single(svc.Environment);
      Assert.Single(svc.Ports);
      Assert.Single(svc.Volumes);
      Assert.Single(svc.Networks);
      Assert.Equal("172.16.0.10",
        svc.Networks.First().IpV4Address);
      Assert.Single(svc.TmpFs);
      Assert.Equal(1048576, svc.TmpFs.First().Size);
      Assert.Single(svc.ConfigLong);
      Assert.Equal("0444", svc.ConfigLong.First().Mode);
    }

    #endregion
  }
}
