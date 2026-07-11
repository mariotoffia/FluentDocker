using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Containers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  // NetworkSettings cluster split out of PodmanContainerParser.cs to keep that file under the
  // 500-line cap (P-M4 XML-doc pass pushed it over). Purely a physical split — same static class.
  public static partial class PodmanContainerParser
  {
    #region NetworkSettings Parsing

    /// <summary>
    /// Parses the <c>NetworkSettings</c> object of a container inspect payload, including nested
    /// <c>Ports</c> and <c>Networks</c> maps.
    /// </summary>
    /// <param name="nsToken">The <c>NetworkSettings</c> property value, or null if absent.</param>
    /// <returns>
    /// The parsed <see cref="ContainerNetworkSettings"/>; <c>null</c> when
    /// <paramref name="nsToken"/> is null or a JSON null/undefined token.
    /// </returns>
    public static ContainerNetworkSettings ParseNetworkSettings(JsonElement? nsToken)
    {
      if (nsToken == null || nsToken.Value.IsNullOrUndefined())
        return null;

      var el = nsToken.Value;
      return new ContainerNetworkSettings
      {
        Bridge = el.GetStringOrDefault("Bridge"),
        SandboxID = el.GetStringOrDefault("SandboxID"),
        HairpinMode = el.GetBoolOrDefault("HairpinMode"),
        LinkLocalIPv6Address = el.GetStringOrDefault("LinkLocalIPv6Address"),
        LinkLocalIPv6PrefixLen = el.GetStringOrDefault("LinkLocalIPv6PrefixLen"),
        SandboxKey = el.GetStringOrDefault("SandboxKey"),
        SecondaryIPAddresses = ParseSecondaryAddresses(el.Prop("SecondaryIPAddresses")),
        SecondaryIPv6Addresses = ParseSecondaryAddresses(el.Prop("SecondaryIPv6Addresses")),
        EndpointID = el.GetStringOrDefault("EndpointID"),
        Gateway = el.GetStringOrDefault("Gateway"),
        GlobalIPv6Address = el.GetStringOrDefault("GlobalIPv6Address"),
        GlobalIPv6PrefixLen = el.GetStringOrDefault("GlobalIPv6PrefixLen"),
        IPAddress = el.GetStringOrDefault("IPAddress"),
        IPPrefixLen = el.GetStringOrDefault("IPPrefixLen"),
        IPv6Gateway = el.GetStringOrDefault("IPv6Gateway"),
        MacAddress = el.GetStringOrDefault("MacAddress"),
        Ports = ParsePorts(el.Prop("Ports")),
        Networks = ParseNetworks(el.Prop("Networks"))
      };
    }

    /// <summary>
    /// Parses the <c>Ports</c> object of a <c>NetworkSettings</c> block (container-port-key to
    /// host-binding-array map).
    /// </summary>
    /// <param name="portsToken">The <c>Ports</c> property value, or null if absent.</param>
    /// <returns>
    /// A dictionary keyed by the container port spec (e.g. <c>"80/tcp"</c>); each value is the
    /// array of <see cref="HostIpEndpoint"/> bindings, or an empty array when the entry has no
    /// bindings. <c>null</c> when <paramref name="portsToken"/> is null or not a JSON object.
    /// </returns>
    public static Dictionary<string, HostIpEndpoint[]> ParsePorts(JsonElement? portsToken)
    {
      if (portsToken == null || portsToken.Value.ValueKind != JsonValueKind.Object)
        return null;

      var result = new Dictionary<string, HostIpEndpoint[]>();
      foreach (var prop in portsToken.Value.EnumerateObject())
      {
        if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
        {
          var bindings = new List<HostIpEndpoint>();
          foreach (var b in prop.Value.EnumerateArray())
          {
            bindings.Add(new HostIpEndpoint
            {
              HostIp = b.GetStringOrDefault("HostIp"),
              HostPort = b.GetStringOrDefault("HostPort")
            });
          }
          result[prop.Name] = [.. bindings];
        }
        else
        {
          result[prop.Name] = [];
        }
      }

      return result;
    }

    /// <summary>
    /// Parses a <c>SecondaryIPAddresses</c>/<c>SecondaryIPv6Addresses</c> array from a
    /// <c>NetworkSettings</c> block.
    /// </summary>
    /// <param name="addressesToken">The array property value, or null if absent.</param>
    /// <returns>
    /// One <see cref="SecondaryAddress"/> per object entry; non-object array entries are skipped
    /// rather than throwing. <c>null</c> (not empty) when <paramref name="addressesToken"/> is
    /// null or not a JSON array.
    /// </returns>
    public static IList<SecondaryAddress> ParseSecondaryAddresses(JsonElement? addressesToken)
    {
      if (addressesToken == null || addressesToken.Value.ValueKind != JsonValueKind.Array)
        return null;

      var result = new List<SecondaryAddress>();
      foreach (var address in addressesToken.Value.EnumerateArray())
      {
        if (address.ValueKind != JsonValueKind.Object)
          continue;

        result.Add(new SecondaryAddress
        {
          Addr = address.GetStringOrDefault("Addr"),
          PrefixLen = address.GetInt32OrDefault("PrefixLen")
        });
      }

      return result;
    }

    /// <summary>
    /// Parses the <c>Networks</c> object of a <c>NetworkSettings</c> block (network-name to
    /// endpoint-details map).
    /// </summary>
    /// <param name="networksToken">The <c>Networks</c> property value, or null if absent.</param>
    /// <returns>
    /// A dictionary keyed by network name, each value a parsed <see cref="BridgeNetwork"/>.
    /// <c>null</c> when <paramref name="networksToken"/> is null or not a JSON object.
    /// </returns>
    public static Dictionary<string, BridgeNetwork> ParseNetworks(JsonElement? networksToken)
    {
      if (networksToken == null || networksToken.Value.ValueKind != JsonValueKind.Object)
        return null;

      var result = new Dictionary<string, BridgeNetwork>();
      foreach (var prop in networksToken.Value.EnumerateObject())
      {
        var n = prop.Value;
        result[prop.Name] = new BridgeNetwork
        {
          NetworkID = n.GetStringOrDefault("NetworkID"),
          EndpointID = n.GetStringOrDefault("EndpointID"),
          Gateway = n.GetStringOrDefault("Gateway"),
          IPAddress = n.GetStringOrDefault("IPAddress"),
          IPPrefixLen = n.GetInt32OrDefault("IPPrefixLen"),
          IPv6Gateway = n.GetStringOrDefault("IPv6Gateway"),
          GlobalIPv6Address = n.GetStringOrDefault("GlobalIPv6Address"),
          GlobalIPv6PrefixLen = n.GetInt32OrDefault("GlobalIPv6PrefixLen"),
          MacAddress = n.GetStringOrDefault("MacAddress"),
          Aliases = ParseStringArray(n.Prop("Aliases"))
        };
      }

      return result;
    }

    #endregion
  }
}
