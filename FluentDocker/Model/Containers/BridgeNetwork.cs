#nullable enable
using System.Text.Json.Serialization;
using FluentDocker.Common;

// ReSharper disable InconsistentNaming

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Per-network endpoint settings for a container, as reported under
  /// <c>NetworkSettings.Networks.&lt;name&gt;</c> in Docker/Podman inspect output.
  /// </summary>
  public sealed class BridgeNetwork
  {
    /// <summary>Network-scoped aliases for this container.</summary>
    public string[]? Aliases { get; set; }

    /// <summary>ID of the network this endpoint belongs to.</summary>
    public string? NetworkID { get; set; }

    /// <summary>ID of this container's endpoint on the network.</summary>
    public string? EndpointID { get; set; }

    /// <summary>IPv4 gateway address on this network.</summary>
    public string? Gateway { get; set; }

    /// <summary>IPv4 address assigned to the container on this network.</summary>
    public string? IPAddress { get; set; }

    /// <summary>
    /// IPv4 prefix length. Stays an <see cref="int"/> for API compatibility; null or unparsable runtime drift reads as 0.
    /// </summary>
    [JsonConverter(typeof(LenientInt32Converter))]
    public int IPPrefixLen { get; set; }

    /// <summary>IPv6 gateway address on this network.</summary>
    public string? IPv6Gateway { get; set; }

    /// <summary>Global-scope IPv6 address assigned to the container on this network.</summary>
    public string? GlobalIPv6Address { get; set; }

    /// <summary>IPv6 prefix length for <see cref="GlobalIPv6Address"/>.</summary>
    [JsonConverter(typeof(LenientInt32Converter))]
    public int GlobalIPv6PrefixLen { get; set; }

    /// <summary>MAC address assigned to the container's interface on this network.</summary>
    public string? MacAddress { get; set; }
  }
}
