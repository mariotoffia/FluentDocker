using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliNetworkDriver JSON parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliNetworkDriverTests
  {
    [Fact]
    public void ParseNetworkList_JsonArray_ReturnsNetworks()
    {
      var json = @"[
                {""Id"":""net1"",""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""},
                {""Id"":""net2"",""Name"":""mynet"",""Driver"":""macvlan"",""Scope"":""local""}
            ]";

      var result = InvokeParseNetworkList(json);
      Assert.Equal(2, result.Count);
      Assert.Equal("net1", result[0].Id);
      Assert.Equal("bridge", result[0].Name);
      Assert.Equal("bridge", result[0].Driver);
      Assert.Equal("net2", result[1].Id);
    }

    [Fact]
    public void ParseNetworkList_NewlineDelimitedJson_ReturnsNetworks()
    {
      var json = "{\"Id\":\"net1\",\"Name\":\"bridge\",\"Driver\":\"bridge\"}\n"
               + "{\"Id\":\"net2\",\"Name\":\"mynet\",\"Driver\":\"macvlan\"}";

      var result = InvokeParseNetworkList(json);
      Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseNetworkList_AlternateKeys_HandlesIDVariant()
    {
      var json = @"[{""ID"":""net1"",""Name"":""bridge"",""Driver"":""bridge""}]";

      var result = InvokeParseNetworkList(json);
      Assert.Single(result);
      Assert.Equal("net1", result[0].Id);
    }

    [Fact]
    public void ParseNetworkList_IPv6Enabled_ParsesCorrectly()
    {
      var json = @"[{""Id"":""net1"",""Name"":""v6net"",""Driver"":""bridge"",""IPv6Enabled"":true}]";

      var result = InvokeParseNetworkList(json);
      Assert.Single(result);
      Assert.True(result[0].IPv6);
    }

    [Fact]
    public void ParseNetworkList_EmptyString_ReturnsEmpty()
    {
      var result = InvokeParseNetworkList("");
      Assert.Empty(result);
    }

    [Fact]
    public void ParseNetworkList_NullString_ReturnsEmpty()
    {
      var result = InvokeParseNetworkList(null!);
      Assert.Empty(result);
    }

    [Fact]
    public void ParseNetworkInspect_JsonArray_ReturnsFirst()
    {
      var json = @"[{""Id"":""net1"",""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""}]";

      var result = InvokeParseNetworkInspect(json);
      Assert.Equal("net1", result.Id);
      Assert.Equal("bridge", result.Name);
      Assert.Equal("bridge", result.Driver);
      Assert.Equal("local", result.Scope);
    }

    [Fact]
    public void ParseNetworkInspect_SingleObject_ReturnsNetwork()
    {
      var json = @"{""Id"":""net1"",""Name"":""mynet"",""Driver"":""macvlan""}";

      var result = InvokeParseNetworkInspect(json);
      Assert.Equal("net1", result.Id);
      Assert.Equal("mynet", result.Name);
    }

    [Fact]
    public void ParseNetworkInspect_WithContainersInterfaces_ReturnsNetworkedContainers()
    {
      var json = @"[{
        ""id"":""net1"",
        ""name"":""mynet"",
        ""driver"":""bridge"",
        ""containers"": {
          ""aabbcc"": {
            ""name"": ""web"",
            ""interfaces"": {
              ""eth0"": {
                ""subnets"": [{ ""ipnet"": ""10.89.0.5/24"", ""gateway"": ""10.89.0.1"" }],
                ""mac_address"": ""02:42:0a:59:00:05""
              }
            }
          }
        }
      }]";

      var result = InvokeParseNetworkInspect(json);

      var container = Assert.Single(result.Containers);
      Assert.Equal("aabbcc", container.Key);
      Assert.Equal("web", container.Value.Name);
      Assert.Equal("10.89.0.5/24", container.Value.IPv4Address);
      Assert.Equal("02:42:0a:59:00:05", container.Value.MacAddress);
    }

    [Fact]
    public void ParseNetworkInspect_DualStackSubnets_PopulatesBothAddresses()
    {
      var json = @"[{
        ""id"":""net1"",
        ""name"":""mynet"",
        ""driver"":""bridge"",
        ""containers"": {
          ""aabbcc"": {
            ""name"": ""web"",
            ""interfaces"": {
              ""eth0"": {
                ""subnets"": [
                  { ""ipnet"": ""10.90.0.2/24"" },
                  { ""ipnet"": ""fd00:dead:beef::2/64"" }
                ],
                ""mac_address"": ""02:42:0a:5a:00:02""
              }
            }
          }
        }
      }]";

      var result = InvokeParseNetworkInspect(json);

      var container = Assert.Single(result.Containers);
      Assert.Equal("10.90.0.2/24", container.Value.IPv4Address);
      Assert.Equal("fd00:dead:beef::2/64", container.Value.IPv6Address);
    }

    [Fact]
    public void ParseNetworkInspect_InvalidJson_Throws()
    {
      // FIX-7: unparseable non-empty network output must fail with diagnostics.
      var ex = Assert.Throws<TargetInvocationException>(() => InvokeParseNetworkInspect("not json"));
      Assert.IsType<FluentDockerException>(ex.InnerException);
    }

    [Fact]
    public void ParseNetworkInspect_InternalNetwork_ParsesCorrectly()
    {
      var json = @"{""Id"":""net1"",""Name"":""internal"",""Internal"":true}";

      var result = InvokeParseNetworkInspect(json);
      Assert.True(result.Internal);
    }

    #region Reflection Helpers

    private static IList<Network> InvokeParseNetworkList(string json)
    {
      var method = typeof(PodmanCliNetworkDriver).GetMethod(
          "ParseNetworkList",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (IList<Network>)method.Invoke(null, [json])!;
    }

    private static Network InvokeParseNetworkInspect(string json)
    {
      var method = typeof(PodmanCliNetworkDriver).GetMethod(
          "ParseNetworkInspect",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (Network)method.Invoke(null, [json])!;
    }

    #endregion
  }
}
