using FluentDocker.Drivers.Podman.Cli.Components;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliContainerDriver parsing of mounts and network settings.
  /// Part 2 of enriched inspect parsing tests.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliContainerParsingNetworkTests
  {
    #region ParseMounts

    [Fact]
    public void ParseMounts_ArrayOfMounts_ParsesAllFields()
    {
      var token = JArray.Parse(@"[
                {
                    ""Name"": ""data-vol"",
                    ""Source"": ""/host/path"",
                    ""Destination"": ""/container/path"",
                    ""Driver"": ""local"",
                    ""Mode"": ""Z"",
                    ""RW"": true,
                    ""Propagation"": ""rprivate""
                },
                {
                    ""Source"": ""/tmp/config"",
                    ""Destination"": ""/etc/config"",
                    ""RW"": false
                }
            ]");

      var mounts = PodmanCliContainerDriver.ParseMounts(token);

      Assert.Equal(2, mounts.Length);
      Assert.Equal("data-vol", mounts[0].Name);
      Assert.Equal("/host/path", mounts[0].Source);
      Assert.Equal("/container/path", mounts[0].Destination);
      Assert.Equal("local", mounts[0].Driver);
      Assert.Equal("Z", mounts[0].Mode);
      Assert.True(mounts[0].RW);
      Assert.Equal("rprivate", mounts[0].Propagation);

      Assert.Null(mounts[1].Name);
      Assert.Equal("/tmp/config", mounts[1].Source);
      Assert.False(mounts[1].RW);
    }

    [Fact]
    public void ParseMounts_NullToken_ReturnsEmptyArray()
    {
      var mounts = PodmanCliContainerDriver.ParseMounts(null);
      Assert.Empty(mounts);
    }

    [Fact]
    public void ParseMounts_EmptyArray_ReturnsEmptyArray()
    {
      var token = JArray.Parse("[]");
      var mounts = PodmanCliContainerDriver.ParseMounts(token);
      Assert.Empty(mounts);
    }

    #endregion

    #region ParseNetworkSettings

    [Fact]
    public void ParseNetworkSettings_FullSettings_ParsesAllFields()
    {
      var token = JObject.Parse(@"{
                ""Bridge"": ""podman0"",
                ""SandboxID"": ""sandbox123"",
                ""HairpinMode"": true,
                ""SandboxKey"": ""/var/run/netns/abc"",
                ""Gateway"": ""10.88.0.1"",
                ""IPAddress"": ""10.88.0.5"",
                ""IPPrefixLen"": ""16"",
                ""MacAddress"": ""02:42:0a:58:00:05"",
                ""Ports"": {
                    ""80/tcp"": [{ ""HostIp"": ""0.0.0.0"", ""HostPort"": ""8080"" }],
                    ""443/tcp"": []
                },
                ""Networks"": {
                    ""bridge"": {
                        ""NetworkID"": ""net1"",
                        ""EndpointID"": ""ep1"",
                        ""Gateway"": ""172.17.0.1"",
                        ""IPAddress"": ""172.17.0.2"",
                        ""IPPrefixLen"": 16,
                        ""MacAddress"": ""02:42:ac:11:00:02"",
                        ""Aliases"": [""web"", ""frontend""]
                    }
                }
            }");

      var ns = PodmanCliContainerDriver.ParseNetworkSettings(token);

      Assert.Equal("podman0", ns.Bridge);
      Assert.Equal("sandbox123", ns.SandboxID);
      Assert.True(ns.HairpinMode);
      Assert.Equal("/var/run/netns/abc", ns.SandboxKey);
      Assert.Equal("10.88.0.1", ns.Gateway);
      Assert.Equal("10.88.0.5", ns.IPAddress);
      Assert.Equal("16", ns.IPPrefixLen);
      Assert.Equal("02:42:0a:58:00:05", ns.MacAddress);

      // Ports
      Assert.Equal(2, ns.Ports.Count);
      Assert.Single(ns.Ports["80/tcp"]);
      Assert.Equal("0.0.0.0", ns.Ports["80/tcp"][0].HostIp);
      Assert.Equal("8080", ns.Ports["80/tcp"][0].HostPort);
      Assert.Empty(ns.Ports["443/tcp"]);

      // Networks
      Assert.Single(ns.Networks);
      var bridge = ns.Networks["bridge"];
      Assert.Equal("net1", bridge.NetworkID);
      Assert.Equal("ep1", bridge.EndpointID);
      Assert.Equal("172.17.0.1", bridge.Gateway);
      Assert.Equal("172.17.0.2", bridge.IPAddress);
      Assert.Equal(16, bridge.IPPrefixLen);
      Assert.Equal("02:42:ac:11:00:02", bridge.MacAddress);
      Assert.Equal(new[] { "web", "frontend" }, bridge.Aliases);
    }

    [Fact]
    public void ParseNetworkSettings_NullToken_ReturnsNull()
    {
      Assert.Null(PodmanCliContainerDriver.ParseNetworkSettings(null));
    }

    [Fact]
    public void ParseNetworkSettings_SingleNetworkPodmanStyle_Parsed()
    {
      var token = JObject.Parse(@"{
                ""Gateway"": ""10.88.0.1"",
                ""IPAddress"": ""10.88.0.2"",
                ""Networks"": {
                    ""podman"": {
                        ""Gateway"": ""10.88.0.1"",
                        ""IPAddress"": ""10.88.0.2"",
                        ""IPPrefixLen"": 16
                    }
                }
            }");

      var ns = PodmanCliContainerDriver.ParseNetworkSettings(token);
      Assert.Equal("10.88.0.2", ns.IPAddress);
      Assert.Single(ns.Networks);
      Assert.Equal(16, ns.Networks["podman"].IPPrefixLen);
    }

    [Fact]
    public void ParseNetworkSettings_NoPortsOrNetworks_NullCollections()
    {
      var token = JObject.Parse(@"{ ""IPAddress"": ""10.0.0.1"" }");
      var ns = PodmanCliContainerDriver.ParseNetworkSettings(token);

      Assert.Equal("10.0.0.1", ns.IPAddress);
      Assert.Null(ns.Ports);
      Assert.Null(ns.Networks);
    }

    #endregion

    #region ParsePorts

    [Fact]
    public void ParsePorts_MultipleBindings_ParsedCorrectly()
    {
      var token = JObject.Parse(@"{
                ""80/tcp"": [
                    { ""HostIp"": ""0.0.0.0"", ""HostPort"": ""8080"" },
                    { ""HostIp"": ""::"", ""HostPort"": ""8080"" }
                ],
                ""3306/tcp"": [{ ""HostIp"": ""127.0.0.1"", ""HostPort"": ""3306"" }]
            }");

      var ports = PodmanCliContainerDriver.ParsePorts(token);

      Assert.Equal(2, ports.Count);
      Assert.Equal(2, ports["80/tcp"].Length);
      Assert.Equal("0.0.0.0", ports["80/tcp"][0].HostIp);
      Assert.Equal("::", ports["80/tcp"][1].HostIp);
      Assert.Single(ports["3306/tcp"]);
      Assert.Equal("127.0.0.1", ports["3306/tcp"][0].HostIp);
    }

    [Fact]
    public void ParsePorts_NullBindings_EmptyArray()
    {
      var token = JObject.Parse(@"{ ""9090/tcp"": null }");
      var ports = PodmanCliContainerDriver.ParsePorts(token);
      Assert.Empty(ports["9090/tcp"]);
    }

    [Fact]
    public void ParsePorts_NullToken_ReturnsNull()
    {
      Assert.Null(PodmanCliContainerDriver.ParsePorts(null));
    }

    #endregion

    #region ParseNetworks

    [Fact]
    public void ParseNetworks_MultipleNetworks_AllParsed()
    {
      var token = JObject.Parse(@"{
                ""frontend"": {
                    ""NetworkID"": ""net-front"",
                    ""IPAddress"": ""10.0.1.2"",
                    ""IPPrefixLen"": 24,
                    ""Gateway"": ""10.0.1.1""
                },
                ""backend"": {
                    ""NetworkID"": ""net-back"",
                    ""IPAddress"": ""10.0.2.2"",
                    ""IPPrefixLen"": 24,
                    ""Gateway"": ""10.0.2.1""
                }
            }");

      var networks = PodmanCliContainerDriver.ParseNetworks(token);

      Assert.Equal(2, networks.Count);
      Assert.Equal("10.0.1.2", networks["frontend"].IPAddress);
      Assert.Equal("10.0.2.2", networks["backend"].IPAddress);
      Assert.Equal(24, networks["frontend"].IPPrefixLen);
    }

    [Fact]
    public void ParseNetworks_NullToken_ReturnsNull()
    {
      Assert.Null(PodmanCliContainerDriver.ParseNetworks(null));
    }

    [Fact]
    public void ParseNetworks_NetworkWithAliases_ParsedCorrectly()
    {
      var token = JObject.Parse(@"{
                ""mynet"": {
                    ""NetworkID"": ""net1"",
                    ""IPAddress"": ""10.0.0.5"",
                    ""IPPrefixLen"": 16,
                    ""Aliases"": [""app"", ""api"", ""service""]
                }
            }");

      var networks = PodmanCliContainerDriver.ParseNetworks(token);
      Assert.Equal(new[] { "app", "api", "service" },
          networks["mynet"].Aliases);
    }

    #endregion
  }
}
