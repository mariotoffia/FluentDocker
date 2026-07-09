#nullable enable
using System.Text.Json.Serialization;
using FluentDocker.Common;

// ReSharper disable InconsistentNaming

namespace FluentDocker.Model.Containers
{
  public sealed class BridgeNetwork
  {
    public string[]? Aliases { get; set; }
    public string? NetworkID { get; set; }
    public string? EndpointID { get; set; }
    public string? Gateway { get; set; }
    public string? IPAddress { get; set; }

    /// <summary>
    /// IPv4 prefix length. Stays an <see cref="int"/> for API compatibility; null or unparsable runtime drift reads as 0.
    /// </summary>
    [JsonConverter(typeof(LenientInt32Converter))]
    public int IPPrefixLen { get; set; }

    public string? IPv6Gateway { get; set; }
    public string? GlobalIPv6Address { get; set; }
    [JsonConverter(typeof(LenientInt32Converter))]
    public int GlobalIPv6PrefixLen { get; set; }
    public string? MacAddress { get; set; }
  }
}
