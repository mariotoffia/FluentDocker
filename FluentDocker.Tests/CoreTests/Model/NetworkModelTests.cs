using System;
using System.Collections.Generic;
using FluentDocker.Model.Networks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class NetworkModelTests
  {
    #region NetworkType Enum

    [Fact]
    public void NetworkType_DefaultValue_IsUnknown()
    {
      // Arrange & Act
      var value = default(NetworkType);

      // Assert
      Assert.Equal(NetworkType.Unknown, value);
    }

    [Theory]
    [InlineData(NetworkType.Unknown, 0)]
    [InlineData(NetworkType.Bridge, 1)]
    [InlineData(NetworkType.Host, 2)]
    [InlineData(NetworkType.Overlay, 3)]
    [InlineData(NetworkType.Ipvlan, 4)]
    [InlineData(NetworkType.Macvlan, 5)]
    [InlineData(NetworkType.None, 6)]
    [InlineData(NetworkType.Custom, 7)]
    public void NetworkType_EnumValues_HaveExpectedIntValues(NetworkType type, int expected)
    {
      // Assert
      Assert.Equal(expected, (int)type);
    }

    [Fact]
    public void NetworkType_HasExactlyEightValues()
    {
      // Arrange & Act
      var values = Enum.GetValues<NetworkType>();

      // Assert
      Assert.Equal(8, values.Length);
    }

    #endregion

    #region NetworkRow

    [Fact]
    public void NetworkRow_DefaultConstruction_AllPropertiesAreDefault()
    {
      // Arrange & Act
      var row = new NetworkRow();

      // Assert
      Assert.Null(row.Id);
      Assert.Null(row.Name);
      Assert.Null(row.Driver);
      Assert.Null(row.Scope);
      Assert.False(row.IPv6);
      Assert.False(row.Internal);
      Assert.Equal(default, row.Created);
    }

    [Fact]
    public void NetworkRow_SetAllProperties_ValuesAreRetained()
    {
      // Arrange
      var created = new DateTime(2026, 3, 27, 10, 30, 0, DateTimeKind.Utc);

      // Act
      var row = new NetworkRow
      {
        Id = "abc123def456",
        Name = "my-bridge-network",
        Driver = "bridge",
        Scope = "local",
        IPv6 = true,
        Internal = true,
        Created = created
      };

      // Assert
      Assert.Equal("abc123def456", row.Id);
      Assert.Equal("my-bridge-network", row.Name);
      Assert.Equal("bridge", row.Driver);
      Assert.Equal("local", row.Scope);
      Assert.True(row.IPv6);
      Assert.True(row.Internal);
      Assert.Equal(created, row.Created);
    }

    #endregion

    #region NetworkedContainer

    [Fact]
    public void NetworkedContainer_DefaultConstruction_AllPropertiesAreNull()
    {
      // Arrange & Act
      var container = new NetworkedContainer();

      // Assert
      Assert.Null(container.Name);
      Assert.Null(container.EndpointID);
      Assert.Null(container.MacAddress);
      Assert.Null(container.IPv4Address);
      Assert.Null(container.IPv6Address);
    }

    [Fact]
    public void NetworkedContainer_SetAllProperties_ValuesAreRetained()
    {
      // Arrange & Act
      var container = new NetworkedContainer
      {
        Name = "web-server",
        EndpointID = "ep-abc123",
        MacAddress = "02:42:ac:11:00:02",
        IPv4Address = "172.17.0.2/16",
        IPv6Address = "fe80::42:acff:fe11:2/64"
      };

      // Assert
      Assert.Equal("web-server", container.Name);
      Assert.Equal("ep-abc123", container.EndpointID);
      Assert.Equal("02:42:ac:11:00:02", container.MacAddress);
      Assert.Equal("172.17.0.2/16", container.IPv4Address);
      Assert.Equal("fe80::42:acff:fe11:2/64", container.IPv6Address);
    }

    [Fact]
    public void NetworkedContainer_CanSetIpv4WithoutIpv6()
    {
      // Arrange & Act
      var container = new NetworkedContainer
      {
        Name = "ipv4-only",
        IPv4Address = "10.0.0.5/24"
      };

      // Assert
      Assert.Equal("10.0.0.5/24", container.IPv4Address);
      Assert.Null(container.IPv6Address);
    }

    #endregion

    #region IpamConfig

    [Fact]
    public void IpamConfig_DefaultConstruction_AllPropertiesAreNull()
    {
      // Arrange & Act
      var config = new IpamConfig();

      // Assert
      Assert.Null(config.Subnet);
      Assert.Null(config.Gateway);
      Assert.Null(config.IPRange);
      Assert.Null(config.AuxiliaryAddresses);
    }

    [Fact]
    public void IpamConfig_SetAllProperties_ValuesAreRetained()
    {
      // Arrange
      var auxAddresses = new Dictionary<string, string>
      {
        ["my-router"] = "172.20.0.254",
        ["my-dns"] = "172.20.0.253"
      };

      // Act
      var config = new IpamConfig
      {
        Subnet = "172.20.0.0/16",
        Gateway = "172.20.0.1",
        IPRange = "172.20.10.0/24",
        AuxiliaryAddresses = auxAddresses
      };

      // Assert
      Assert.Equal("172.20.0.0/16", config.Subnet);
      Assert.Equal("172.20.0.1", config.Gateway);
      Assert.Equal("172.20.10.0/24", config.IPRange);
      Assert.Equal(2, config.AuxiliaryAddresses.Count);
      Assert.Equal("172.20.0.254", config.AuxiliaryAddresses["my-router"]);
      Assert.Equal("172.20.0.253", config.AuxiliaryAddresses["my-dns"]);
    }

    [Fact]
    public void IpamConfig_SubnetOnly_OtherFieldsRemainNull()
    {
      // Arrange & Act
      var config = new IpamConfig { Subnet = "10.0.0.0/8" };

      // Assert
      Assert.Equal("10.0.0.0/8", config.Subnet);
      Assert.Null(config.Gateway);
      Assert.Null(config.IPRange);
      Assert.Null(config.AuxiliaryAddresses);
    }

    #endregion

    #region Ipam

    [Fact]
    public void Ipam_DefaultConstruction_AllPropertiesAreNull()
    {
      // Arrange & Act
      var ipam = new Ipam();

      // Assert
      Assert.Null(ipam.Driver);
      Assert.Null(ipam.Options);
      Assert.Null(ipam.Config);
    }

    [Fact]
    public void Ipam_SetAllProperties_ValuesAreRetained()
    {
      // Arrange
      var options = new Dictionary<string, string> { ["foo"] = "bar" };
      var configs = new List<IpamConfig>
      {
        new IpamConfig { Subnet = "172.20.0.0/16", Gateway = "172.20.0.1" }
      };

      // Act
      var ipam = new Ipam
      {
        Driver = "default",
        Options = options,
        Config = configs
      };

      // Assert
      Assert.Equal("default", ipam.Driver);
      Assert.Single(ipam.Config);
      Assert.Equal("172.20.0.0/16", ipam.Config[0].Subnet);
      Assert.Equal("172.20.0.1", ipam.Config[0].Gateway);
      Assert.Equal("bar", ipam.Options["foo"]);
    }

    [Fact]
    public void Ipam_WithMultipleConfigs_AllConfigsAccessible()
    {
      // Arrange & Act
      var ipam = new Ipam
      {
        Driver = "default",
        Config = new List<IpamConfig>
        {
          new IpamConfig { Subnet = "10.0.0.0/24", Gateway = "10.0.0.1" },
          new IpamConfig { Subnet = "10.1.0.0/24", Gateway = "10.1.0.1" },
          new IpamConfig { Subnet = "10.2.0.0/24", Gateway = "10.2.0.1" }
        }
      };

      // Assert
      Assert.Equal(3, ipam.Config.Count);
      Assert.Equal("10.0.0.0/24", ipam.Config[0].Subnet);
      Assert.Equal("10.1.0.0/24", ipam.Config[1].Subnet);
      Assert.Equal("10.2.0.0/24", ipam.Config[2].Subnet);
    }

    [Fact]
    public void Ipam_ConfigListIsMutable_CanAddAfterCreation()
    {
      // Arrange
      var ipam = new Ipam { Config = new List<IpamConfig>() };

      // Act
      ipam.Config.Add(new IpamConfig { Subnet = "192.168.0.0/16" });

      // Assert
      Assert.Single(ipam.Config);
      Assert.Equal("192.168.0.0/16", ipam.Config[0].Subnet);
    }

    #endregion

    #region NetworkConfiguration

    [Fact]
    public void NetworkConfiguration_DefaultConstruction_BoolsAreFalseAndRefsAreNull()
    {
      // Arrange & Act
      var config = new NetworkConfiguration();

      // Assert
      Assert.Null(config.Name);
      Assert.Null(config.Id);
      Assert.Equal(default, config.Created);
      Assert.Null(config.Scope);
      Assert.Null(config.Driver);
      Assert.False(config.EnableIPv6);
      Assert.False(config.Internal);
      Assert.False(config.Attachable);
      Assert.False(config.Ingress);
      Assert.False(config.ConfigOnly);
      Assert.Null(config.IPAM);
      Assert.Null(config.ConfigFrom);
      Assert.Null(config.Containers);
      Assert.Null(config.Options);
    }

    [Fact]
    public void NetworkConfiguration_SetAllScalarProperties_ValuesAreRetained()
    {
      // Arrange
      var created = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);

      // Act
      var config = new NetworkConfiguration
      {
        Name = "prod-network",
        Id = "sha256:abcdef1234567890",
        Created = created,
        Scope = "local",
        Driver = "bridge",
        EnableIPv6 = true,
        Internal = true,
        Attachable = true,
        Ingress = true,
        ConfigOnly = true
      };

      // Assert
      Assert.Equal("prod-network", config.Name);
      Assert.Equal("sha256:abcdef1234567890", config.Id);
      Assert.Equal(created, config.Created);
      Assert.Equal("local", config.Scope);
      Assert.Equal("bridge", config.Driver);
      Assert.True(config.EnableIPv6);
      Assert.True(config.Internal);
      Assert.True(config.Attachable);
      Assert.True(config.Ingress);
      Assert.True(config.ConfigOnly);
    }

    [Fact]
    public void NetworkConfiguration_WithIpam_IpamIsFullyAccessible()
    {
      // Arrange & Act
      var config = new NetworkConfiguration
      {
        Name = "custom-net",
        Driver = "bridge",
        IPAM = new Ipam
        {
          Driver = "default",
          Config = new List<IpamConfig>
          {
            new IpamConfig
            {
              Subnet = "172.28.0.0/16",
              Gateway = "172.28.0.1",
              IPRange = "172.28.5.0/24"
            }
          }
        }
      };

      // Assert
      Assert.NotNull(config.IPAM);
      Assert.Equal("default", config.IPAM.Driver);
      Assert.Single(config.IPAM.Config);
      Assert.Equal("172.28.0.0/16", config.IPAM.Config[0].Subnet);
      Assert.Equal("172.28.0.1", config.IPAM.Config[0].Gateway);
      Assert.Equal("172.28.5.0/24", config.IPAM.Config[0].IPRange);
    }

    [Fact]
    public void NetworkConfiguration_WithContainers_ContainersAreAccessibleById()
    {
      // Arrange
      var container1 = new NetworkedContainer
      {
        Name = "web",
        EndpointID = "ep1",
        IPv4Address = "172.28.0.2/16"
      };
      var container2 = new NetworkedContainer
      {
        Name = "db",
        EndpointID = "ep2",
        IPv4Address = "172.28.0.3/16"
      };

      // Act
      var config = new NetworkConfiguration
      {
        Name = "app-network",
        Containers = new Dictionary<string, NetworkedContainer>
        {
          ["container-id-1"] = container1,
          ["container-id-2"] = container2
        }
      };

      // Assert
      Assert.Equal(2, config.Containers.Count);
      Assert.Equal("web", config.Containers["container-id-1"].Name);
      Assert.Equal("db", config.Containers["container-id-2"].Name);
      Assert.Equal("172.28.0.2/16", config.Containers["container-id-1"].IPv4Address);
    }

    [Fact]
    public void NetworkConfiguration_WithOptions_OptionsAreAccessible()
    {
      // Arrange & Act
      var config = new NetworkConfiguration
      {
        Options = new Dictionary<string, string>
        {
          ["com.docker.network.bridge.default_bridge"] = "true",
          ["com.docker.network.bridge.enable_icc"] = "true",
          ["com.docker.network.driver.mtu"] = "1500"
        }
      };

      // Assert
      Assert.Equal(3, config.Options.Count);
      Assert.Equal("true", config.Options["com.docker.network.bridge.default_bridge"]);
      Assert.Equal("1500", config.Options["com.docker.network.driver.mtu"]);
    }

    [Fact]
    public void NetworkConfiguration_FullNetworkInspectModel_AllRelationshipsWork()
    {
      // Arrange & Act -- simulates a full Docker network inspect response
      var config = new NetworkConfiguration
      {
        Name = "my-overlay",
        Id = "7d86d31b0532",
        Created = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
        Scope = "swarm",
        Driver = "overlay",
        EnableIPv6 = false,
        Internal = false,
        Attachable = true,
        Ingress = false,
        ConfigOnly = false,
        IPAM = new Ipam
        {
          Driver = "default",
          Options = new Dictionary<string, string>(),
          Config = new List<IpamConfig>
          {
            new IpamConfig
            {
              Subnet = "10.0.9.0/24",
              Gateway = "10.0.9.1"
            }
          }
        },
        Containers = new Dictionary<string, NetworkedContainer>
        {
          ["aabbcc"] = new NetworkedContainer
          {
            Name = "task.1",
            EndpointID = "ep-aabb",
            MacAddress = "02:42:0a:00:09:03",
            IPv4Address = "10.0.9.3/24",
            IPv6Address = ""
          }
        },
        Options = new Dictionary<string, string>
        {
          ["com.docker.network.driver.overlay.vxlanid_list"] = "4097"
        },
        ConfigFrom = new Dictionary<string, string>()
      };

      // Assert -- verify the full object graph
      Assert.Equal("my-overlay", config.Name);
      Assert.Equal("overlay", config.Driver);
      Assert.True(config.Attachable);
      Assert.False(config.Ingress);

      Assert.NotNull(config.IPAM);
      Assert.Single(config.IPAM.Config);
      Assert.Equal("10.0.9.0/24", config.IPAM.Config[0].Subnet);

      Assert.Single(config.Containers);
      Assert.Equal("task.1", config.Containers["aabbcc"].Name);
      Assert.Equal("02:42:0a:00:09:03", config.Containers["aabbcc"].MacAddress);
      Assert.Single(config.Options);
      Assert.Empty(config.ConfigFrom);
    }

    #endregion
  }
}
