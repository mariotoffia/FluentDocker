using System.Collections.Generic;
using System.Linq;
using FluentDocker.Model.Compose;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class ComposeModelTests
  {
    #region Enum Tests

    [Theory]
    [InlineData(VolumeType.Volume, 0)]
    [InlineData(VolumeType.Bind, 1)]
    [InlineData(VolumeType.TmpFs, 2)]
    public void VolumeType_HasExpectedValues(VolumeType value, int expected)
    {
      Assert.Equal(expected, (int)value);
    }

    [Fact]
    public void VolumeType_HasExactlyThreeMembers()
    {
      var values = System.Enum.GetValues<VolumeType>();
      Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(PortMode.Host, 0)]
    [InlineData(PortMode.Ingress, 1)]
    public void PortMode_HasExpectedValues(PortMode value, int expected)
    {
      Assert.Equal(expected, (int)value);
    }

    [Fact]
    public void PortMode_HasExactlyTwoMembers()
    {
      var values = System.Enum.GetValues<PortMode>();
      Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(ContainerIsolationType.Default, 0)]
    [InlineData(ContainerIsolationType.Process, 1)]
    [InlineData(ContainerIsolationType.HyperV, 2)]
    public void ContainerIsolationType_HasExpectedValues(
      ContainerIsolationType value, int expected)
    {
      Assert.Equal(expected, (int)value);
    }

    [Fact]
    public void ContainerIsolationType_HasExactlyThreeMembers()
    {
      var values = System.Enum.GetValues<ContainerIsolationType>();
      Assert.Equal(3, values.Length);
    }

    #endregion

    #region ComposeServiceDefinition Tests

    [Fact]
    public void ComposeServiceDefinition_DefaultConstruction_HasEmptyCollections()
    {
      var svc = new ComposeServiceDefinition();

      Assert.Null(svc.Name);
      Assert.Null(svc.Build);
      Assert.Null(svc.Command);
      Assert.Null(svc.Image);
      Assert.Null(svc.HealthCheck);
      Assert.Null(svc.Logging);
      Assert.Null(svc.NetworkMode);
      Assert.Null(svc.CgroupParent);
      Assert.Null(svc.ContainerName);
      Assert.Null(svc.CredentialSpec);
      Assert.Null(svc.Init);
      Assert.Null(svc.StopGracePeriod);
      Assert.Null(svc.StopSignal);
      Assert.Null(svc.SysCtls);
      Assert.Null(svc.Ulimits);
      Assert.Null(svc.ConfigsShort);

      Assert.NotNull(svc.CapAdd);
      Assert.Empty(svc.CapAdd);
      Assert.NotNull(svc.CapDrop);
      Assert.Empty(svc.CapDrop);
      Assert.NotNull(svc.ConfigLong);
      Assert.Empty(svc.ConfigLong);
      Assert.NotNull(svc.Devices);
      Assert.Empty(svc.Devices);
      Assert.NotNull(svc.DependsOn);
      Assert.Empty(svc.DependsOn);
      Assert.NotNull(svc.Dns);
      Assert.Empty(svc.Dns);
      Assert.NotNull(svc.DnsSearch);
      Assert.Empty(svc.DnsSearch);
      Assert.NotNull(svc.TmpFs);
      Assert.Empty(svc.TmpFs);
      Assert.NotNull(svc.EntryPoint);
      Assert.Empty(svc.EntryPoint);
      Assert.NotNull(svc.EnvFiles);
      Assert.Empty(svc.EnvFiles);
      Assert.NotNull(svc.Environment);
      Assert.Empty(svc.Environment);
      Assert.NotNull(svc.ExposePorts);
      Assert.Empty(svc.ExposePorts);
      Assert.NotNull(svc.ExternalLinks);
      Assert.Empty(svc.ExternalLinks);
      Assert.NotNull(svc.ExtraHosts);
      Assert.Empty(svc.ExtraHosts);
      Assert.NotNull(svc.Labels);
      Assert.Empty(svc.Labels);
      Assert.NotNull(svc.Networks);
      Assert.Empty(svc.Networks);
      Assert.NotNull(svc.Ports);
      Assert.Empty(svc.Ports);
      Assert.NotNull(svc.Secrets);
      Assert.Empty(svc.Secrets);
      Assert.NotNull(svc.SecurityOpt);
      Assert.Empty(svc.SecurityOpt);
      Assert.NotNull(svc.Volumes);
      Assert.Empty(svc.Volumes);
    }

    [Fact]
    public void ComposeServiceDefinition_DefaultBooleans_AreFalse()
    {
      var svc = new ComposeServiceDefinition();
      Assert.False(svc.PidModeHost);
      Assert.False(svc.DisableUserNamespaceMode);
    }

    [Fact]
    public void ComposeServiceDefinition_DefaultIsolation_IsDefault()
    {
      var svc = new ComposeServiceDefinition();
      Assert.Equal(ContainerIsolationType.Default, svc.Isolation);
    }

    [Fact]
    public void ComposeServiceDefinition_SetProperties_RoundTrips()
    {
      var svc = new ComposeServiceDefinition
      {
        Name = "web",
        Image = "nginx:latest",
        Command = "nginx -g 'daemon off;'",
        ContainerName = "my-nginx",
        NetworkMode = "bridge",
        PidModeHost = true,
        DisableUserNamespaceMode = true,
        Isolation = ContainerIsolationType.Process,
        StopGracePeriod = "30s",
        StopSignal = "SIGQUIT",
        CgroupParent = "m-executor-abcd",
        CredentialSpec = "file:my-spec.json",
        Init = "true"
      };

      Assert.Equal("web", svc.Name);
      Assert.Equal("nginx:latest", svc.Image);
      Assert.Equal("nginx -g 'daemon off;'", svc.Command);
      Assert.Equal("my-nginx", svc.ContainerName);
      Assert.Equal("bridge", svc.NetworkMode);
      Assert.True(svc.PidModeHost);
      Assert.True(svc.DisableUserNamespaceMode);
      Assert.Equal(ContainerIsolationType.Process, svc.Isolation);
      Assert.Equal("30s", svc.StopGracePeriod);
      Assert.Equal("SIGQUIT", svc.StopSignal);
      Assert.Equal("m-executor-abcd", svc.CgroupParent);
      Assert.Equal("file:my-spec.json", svc.CredentialSpec);
      Assert.Equal("true", svc.Init);
    }

    [Fact]
    public void ComposeServiceDefinition_Collections_CanAddElements()
    {
      var svc = new ComposeServiceDefinition();

      svc.CapAdd.Add("NET_ADMIN");
      svc.CapDrop.Add("SYS_ADMIN");
      svc.DependsOn.Add("db");
      svc.DependsOn.Add("redis");
      svc.Dns.Add("8.8.8.8");
      svc.DnsSearch.Add("dc1.example.com");
      svc.Devices.Add("/dev/ttyUSB0:/dev/ttyUSB0");
      svc.EntryPoint.Add("/bin/sh");
      svc.EntryPoint.Add("-c");
      svc.EnvFiles.Add("./common.env");
      svc.Environment["RACK_ENV"] = "development";
      svc.ExposePorts.Add("3000");
      svc.ExternalLinks.Add("redis_1");
      svc.ExtraHosts["somehost"] = "162.242.195.82";
      svc.Labels["com.example.department"] = "Finance";
      svc.SecurityOpt.Add("label:user:USER");

      Assert.Single(svc.CapAdd);
      Assert.Contains("NET_ADMIN", svc.CapAdd);
      Assert.Single(svc.CapDrop);
      Assert.Contains("SYS_ADMIN", svc.CapDrop);
      Assert.Equal(2, svc.DependsOn.Count);
      Assert.Single(svc.Dns);
      Assert.Single(svc.DnsSearch);
      Assert.Single(svc.Devices);
      Assert.Equal(2, svc.EntryPoint.Count);
      Assert.Single(svc.EnvFiles);
      Assert.Single(svc.Environment);
      Assert.Equal("development", svc.Environment["RACK_ENV"]);
      Assert.Single(svc.ExposePorts);
      Assert.Single(svc.ExternalLinks);
      Assert.Single(svc.ExtraHosts);
      Assert.Equal("162.242.195.82", svc.ExtraHosts["somehost"]);
      Assert.Single(svc.Labels);
      Assert.Single(svc.SecurityOpt);
    }

    [Fact]
    public void ComposeServiceDefinition_PolymorphicPorts_AcceptsBothShortAndLong()
    {
      var svc = new ComposeServiceDefinition();

      svc.Ports.Add(new PortsShortDefinition { Entry = "8080:80" });
      svc.Ports.Add(new PortsLongDefinition
      {
        Target = 443,
        Published = 8443,
        Protocol = "tcp",
        Mode = PortMode.Host
      });

      Assert.Equal(2, svc.Ports.Count);
      Assert.IsType<PortsShortDefinition>(svc.Ports[0]);
      Assert.IsType<PortsLongDefinition>(svc.Ports[1]);
    }

    [Fact]
    public void ComposeServiceDefinition_PolymorphicSecrets_AcceptsBothShortAndLong()
    {
      var svc = new ComposeServiceDefinition();

      svc.Secrets.Add(new ShortSecret { Name = "db_password" });
      svc.Secrets.Add(new LongSecret
      {
        Source = "my_secret",
        Target = "redis_secret",
        Uid = 103,
        Gid = 103,
        Mode = "0440"
      });

      Assert.Equal(2, svc.Secrets.Count);
      Assert.IsType<ShortSecret>(svc.Secrets[0]);
      Assert.IsType<LongSecret>(svc.Secrets[1]);
    }

    [Fact]
    public void ComposeServiceDefinition_PolymorphicVolumes_AcceptsBothShortAndLong()
    {
      var svc = new ComposeServiceDefinition();

      svc.Volumes.Add(new ShortServiceVolumeDefinition
      {
        Entry = "/var/lib/mysql"
      });
      svc.Volumes.Add(new LongServiceVolumeDefinition
      {
        Source = "mydata",
        Target = "/data",
        Type = VolumeType.Volume
      });

      Assert.Equal(2, svc.Volumes.Count);
      Assert.IsType<ShortServiceVolumeDefinition>(svc.Volumes[0]);
      Assert.IsType<LongServiceVolumeDefinition>(svc.Volumes[1]);
    }

    #endregion

    #region ComposeVolumeDefinition Tests

    [Fact]
    public void ComposeVolumeDefinition_DefaultConstruction_NameIsNull()
    {
      var vol = new ComposeVolumeDefinition();
      Assert.Null(vol.Name);
    }

    [Fact]
    public void ComposeVolumeDefinition_SetName_RoundTrips()
    {
      var vol = new ComposeVolumeDefinition { Name = "db-data" };
      Assert.Equal("db-data", vol.Name);
    }

    #endregion

    #region BuildDefinition Tests

    [Fact]
    public void BuildDefinition_DefaultConstruction_HasEmptyCollections()
    {
      var build = new BuildDefinition();

      Assert.Null(build.Context);
      Assert.Null(build.Dockerfile);
      Assert.Null(build.ShmSize);
      Assert.Null(build.Target);
      Assert.NotNull(build.Args);
      Assert.Empty(build.Args);
      Assert.NotNull(build.CacheFrom);
      Assert.Empty(build.CacheFrom);
      Assert.NotNull(build.Labels);
      Assert.Empty(build.Labels);
    }

    [Fact]
    public void BuildDefinition_SetProperties_RoundTrips()
    {
      var build = new BuildDefinition
      {
        Context = "./src",
        Dockerfile = "Dockerfile.prod",
        ShmSize = "2gb",
        Target = "prod"
      };

      build.Args.Add("buildno=1");
      build.Args.Add("gitcommithash=cdc3b19");
      build.CacheFrom.Add("alpine:latest");
      build.Labels["com.example.description"] = "Accounting webapp";

      Assert.Equal("./src", build.Context);
      Assert.Equal("Dockerfile.prod", build.Dockerfile);
      Assert.Equal("2gb", build.ShmSize);
      Assert.Equal("prod", build.Target);
      Assert.Equal(2, build.Args.Count);
      Assert.Single(build.CacheFrom);
      Assert.Single(build.Labels);
    }

    #endregion
  }
}
