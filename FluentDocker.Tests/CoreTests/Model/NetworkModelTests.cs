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

  }
}
