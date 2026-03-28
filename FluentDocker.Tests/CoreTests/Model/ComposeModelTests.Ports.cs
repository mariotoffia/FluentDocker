using System.Collections.Generic;
using FluentDocker.Model.Compose;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class ComposeModelTests
  {
    #region PortsShortDefinition Tests

    [Fact]
    public void PortsShortDefinition_DefaultConstruction_HasNullEntry()
    {
      var port = new PortsShortDefinition();
      Assert.Null(port.Entry);
    }

    [Theory]
    [InlineData("3000")]
    [InlineData("3000-3005")]
    [InlineData("8000:8000")]
    [InlineData("9090-9091:8080-8081")]
    [InlineData("49100:22")]
    [InlineData("127.0.0.1:8001:8001")]
    [InlineData("127.0.0.1:5000-5010:5000-5010")]
    [InlineData("6060:6060/udp")]
    public void PortsShortDefinition_Entry_AcceptsAllFormats(string entry)
    {
      var port = new PortsShortDefinition { Entry = entry };
      Assert.Equal(entry, port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_ImplementsIPortsDefinition()
    {
      IPortsDefinition port = new PortsShortDefinition { Entry = "80:80" };
      Assert.IsType<PortsShortDefinition>(port);
    }

    [Fact]
    public void PortsShortDefinition_ContainerPortOnly_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "3000" };
      Assert.Equal("3000", port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_HostAndContainerPort_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "8080:80" };
      Assert.Equal("8080:80", port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_WithProtocol_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "8080:80/tcp" };
      Assert.Equal("8080:80/tcp", port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_WithIpAddress_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "127.0.0.1:8001:8001" };
      Assert.Equal("127.0.0.1:8001:8001", port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_PortRange_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "9090-9091:8080-8081" };
      Assert.Equal("9090-9091:8080-8081", port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_UdpProtocol_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "6060:6060/udp" };
      Assert.Equal("6060:6060/udp", port.Entry);
    }

    [Fact]
    public void PortsShortDefinition_IpWithRange_StoresEntry()
    {
      var port = new PortsShortDefinition { Entry = "127.0.0.1:5000-5010:5000-5010" };
      Assert.Equal("127.0.0.1:5000-5010:5000-5010", port.Entry);
    }

    #endregion

    #region PortsLongDefinition Tests

    [Fact]
    public void PortsLongDefinition_DefaultConstruction_HasExpectedDefaults()
    {
      var port = new PortsLongDefinition();

      Assert.Equal(0, port.Target);
      Assert.Equal(0, port.Published);
      Assert.Equal("tcp", port.Protocol);
      Assert.Equal(PortMode.Host, port.Mode);
    }

    [Fact]
    public void PortsLongDefinition_SetProperties_RoundTrips()
    {
      var port = new PortsLongDefinition
      {
        Target = 80,
        Published = 8080,
        Protocol = "tcp",
        Mode = PortMode.Host
      };

      Assert.Equal(80, port.Target);
      Assert.Equal(8080, port.Published);
      Assert.Equal("tcp", port.Protocol);
      Assert.Equal(PortMode.Host, port.Mode);
    }

    [Fact]
    public void PortsLongDefinition_UdpProtocol_RoundTrips()
    {
      var port = new PortsLongDefinition
      {
        Target = 53,
        Published = 53,
        Protocol = "udp",
        Mode = PortMode.Host
      };

      Assert.Equal("udp", port.Protocol);
    }

    [Fact]
    public void PortsLongDefinition_IngressMode_RoundTrips()
    {
      var port = new PortsLongDefinition
      {
        Target = 80,
        Published = 8080,
        Mode = PortMode.Ingress
      };

      Assert.Equal(PortMode.Ingress, port.Mode);
    }

    [Fact]
    public void PortsLongDefinition_ImplementsIPortsDefinition()
    {
      IPortsDefinition port = new PortsLongDefinition
      {
        Target = 80,
        Published = 8080
      };
      Assert.IsType<PortsLongDefinition>(port);
    }

    [Fact]
    public void PortsLongDefinition_DefaultProtocol_IsTcp()
    {
      var port = new PortsLongDefinition();
      Assert.Equal("tcp", port.Protocol);
    }

    [Fact]
    public void PortsLongDefinition_DefaultMode_IsHost()
    {
      var port = new PortsLongDefinition();
      Assert.Equal(PortMode.Host, port.Mode);
    }

    #endregion

    #region Ports in Service Context Tests

    [Fact]
    public void ServicePorts_MixedShortAndLong_CanBeFiltered()
    {
      var svc = new ComposeServiceDefinition();
      svc.Ports.Add(new PortsShortDefinition { Entry = "8080:80" });
      svc.Ports.Add(new PortsShortDefinition { Entry = "8443:443" });
      svc.Ports.Add(new PortsLongDefinition
      {
        Target = 9090,
        Published = 9090,
        Protocol = "tcp",
        Mode = PortMode.Ingress
      });

      var shortPorts = new List<PortsShortDefinition>();
      var longPorts = new List<PortsLongDefinition>();

      foreach (var port in svc.Ports)
      {
        if (port is PortsShortDefinition shortPort)
          shortPorts.Add(shortPort);
        else if (port is PortsLongDefinition longPort)
          longPorts.Add(longPort);
      }

      Assert.Equal(2, shortPorts.Count);
      Assert.Single(longPorts);
      Assert.Equal("8080:80", shortPorts[0].Entry);
      Assert.Equal("8443:443", shortPorts[1].Entry);
      Assert.Equal(9090, longPorts[0].Target);
      Assert.Equal(PortMode.Ingress, longPorts[0].Mode);
    }

    [Fact]
    public void ServicePorts_AllShort_CanAddMultiple()
    {
      var svc = new ComposeServiceDefinition();

      string[] entries =
      {
        "3000",
        "8000:8000",
        "49100:22",
        "6060:6060/udp"
      };

      foreach (var e in entries)
        svc.Ports.Add(new PortsShortDefinition { Entry = e });

      Assert.Equal(4, svc.Ports.Count);

      for (var i = 0; i < entries.Length; i++)
      {
        var shortPort = Assert.IsType<PortsShortDefinition>(svc.Ports[i]);
        Assert.Equal(entries[i], shortPort.Entry);
      }
    }

    [Fact]
    public void ServicePorts_AllLong_MultiProtocol()
    {
      var svc = new ComposeServiceDefinition();

      svc.Ports.Add(new PortsLongDefinition
      {
        Target = 80,
        Published = 8080,
        Protocol = "tcp"
      });
      svc.Ports.Add(new PortsLongDefinition
      {
        Target = 53,
        Published = 5353,
        Protocol = "udp"
      });
      svc.Ports.Add(new PortsLongDefinition
      {
        Target = 443,
        Published = 8443,
        Protocol = "tcp",
        Mode = PortMode.Ingress
      });

      Assert.Equal(3, svc.Ports.Count);

      var first = Assert.IsType<PortsLongDefinition>(svc.Ports[0]);
      Assert.Equal("tcp", first.Protocol);
      Assert.Equal(PortMode.Host, first.Mode);

      var second = Assert.IsType<PortsLongDefinition>(svc.Ports[1]);
      Assert.Equal("udp", second.Protocol);

      var third = Assert.IsType<PortsLongDefinition>(svc.Ports[2]);
      Assert.Equal(PortMode.Ingress, third.Mode);
    }

    #endregion

    #region PortMode Enum Tests

    [Fact]
    public void PortMode_Host_IsDefault()
    {
      var port = new PortsLongDefinition();
      Assert.Equal(PortMode.Host, port.Mode);
    }

    [Fact]
    public void PortMode_AllValues_CanBeAssigned()
    {
      var port = new PortsLongDefinition { Mode = PortMode.Host };
      Assert.Equal(PortMode.Host, port.Mode);

      port.Mode = PortMode.Ingress;
      Assert.Equal(PortMode.Ingress, port.Mode);
    }

    #endregion
  }
}
