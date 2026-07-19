using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public class DockerCliNetworkInspectTests
  {
    [Fact]
    public void InspectJson_WithContainers_PopulatesNetworkContainers()
    {
      var json = @"[
        {
          ""Id"":""net123"",
          ""Name"":""bridge"",
          ""Containers"": {
            ""aabbcc"": {
              ""Name"": ""web"",
              ""EndpointID"": ""endpoint-1"",
              ""MacAddress"": ""02:42:ac:11:00:02"",
              ""IPv4Address"": ""172.17.0.2/16"",
              ""IPv6Address"": """"
            }
          }
        }
      ]";

      var networks = JsonSerializer.Deserialize<List<Network>>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(networks);
      var container = Assert.Single(networks[0].Containers);
      Assert.Equal("aabbcc", container.Key);
      Assert.Equal("web", container.Value.Name);
      Assert.Equal("endpoint-1", container.Value.EndpointID);
      Assert.Equal("02:42:ac:11:00:02", container.Value.MacAddress);
      Assert.Equal("172.17.0.2/16", container.Value.IPv4Address);
      Assert.Equal("", container.Value.IPv6Address);
    }
  }
}
