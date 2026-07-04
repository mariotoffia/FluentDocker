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
    public void Address_InvalidHostIp_ThrowsInvalidOperationException()
    {
      var endpoint = new HostIpEndpoint
      {
        HostIp = "not-an-ip-address"
      };

      Assert.Throws<InvalidOperationException>(() => endpoint.Address);
    }

    [Theory]
    [InlineData("not-a-port")]
    [InlineData("70000")]
    public void Port_InvalidHostPort_ThrowsInvalidOperationException(string hostPort)
    {
      var endpoint = new HostIpEndpoint
      {
        HostPort = hostPort
      };

      Assert.Throws<InvalidOperationException>(() => endpoint.Port);
    }

    [Fact]
    public void Port_WithoutHostPort_ThrowsInvalidOperationException()
    {
      var endpoint = new HostIpEndpoint();

      Assert.Throws<InvalidOperationException>(() => endpoint.Port);
    }
  }
}
