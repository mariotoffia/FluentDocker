using System;
using System.Text.Json;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class HostIpEndpointTests
  {
    [Fact]
    public void Serialize_WithHostIpAndPort_WritesDockerBindingJson()
    {
      var json = JsonSerializer.Serialize(new HostIpEndpoint
      {
        HostIp = "127.0.0.1",
        HostPort = "8080"
      });

      Assert.Contains(@"""HostIp"":""127.0.0.1""", json);
      Assert.Contains(@"""HostPort"":""8080""", json);
    }

    [Fact]
    public void HostIp_InvalidAddress_ThrowsArgumentException()
    {
      var endpoint = new HostIpEndpoint();

      Assert.Throws<ArgumentException>(() => endpoint.HostIp = "not-an-ip-address");
    }

    [Theory]
    [InlineData("not-a-port")]
    [InlineData("70000")]
    public void HostPort_InvalidPort_ThrowsArgumentException(string hostPort)
    {
      var endpoint = new HostIpEndpoint();

      Assert.Throws<ArgumentException>(() => endpoint.HostPort = hostPort);
    }

    [Fact]
    public void Port_WithoutHostPort_ThrowsInvalidOperationException()
    {
      var endpoint = new HostIpEndpoint();

      Assert.Throws<InvalidOperationException>(() => endpoint.Port);
    }
  }
}
