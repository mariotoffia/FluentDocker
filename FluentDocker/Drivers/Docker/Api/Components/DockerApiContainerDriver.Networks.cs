using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Containers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiContainerDriver
  {
    private static Dictionary<string, BridgeNetwork>? ParseBridgeNetworks(JsonElement? networks)
    {
      if (networks?.ValueKind != JsonValueKind.Object)
        return null;

      var result = new Dictionary<string, BridgeNetwork>();
      foreach (var property in networks.Value.EnumerateObject())
      {
        var value = property.Value;
        if (value.ValueKind != JsonValueKind.Object)
          continue;
        result[property.Name] = new BridgeNetwork
        {
          Aliases = value.GetStringArray("Aliases"),
          NetworkID = value.GetStringOrDefault("NetworkID"),
          EndpointID = value.GetStringOrDefault("EndpointID"),
          Gateway = value.GetStringOrDefault("Gateway"),
          IPAddress = value.GetStringOrDefault("IPAddress"),
          IPPrefixLen = value.GetInt32OrDefault("IPPrefixLen"),
          IPv6Gateway = value.GetStringOrDefault("IPv6Gateway"),
          GlobalIPv6Address = value.GetStringOrDefault("GlobalIPv6Address"),
          GlobalIPv6PrefixLen = value.GetInt32OrDefault("GlobalIPv6PrefixLen"),
          MacAddress = value.GetStringOrDefault("MacAddress")
        };
      }
      return result;
    }
  }
}
