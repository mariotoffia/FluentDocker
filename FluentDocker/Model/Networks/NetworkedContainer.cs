#nullable enable
namespace FluentDocker.Model.Networks
{
  /// <summary>
  /// A single entry from a network's <c>Containers</c> map, as returned by
  /// <c>docker network inspect</c> / <c>podman network inspect</c>.
  /// </summary>
  public sealed class NetworkedContainer
  {
    /// <summary>Name of the connected container.</summary>
    public string? Name { get; set; }

    /// <summary>ID of the container's endpoint on this network.</summary>
    // ReSharper disable once InconsistentNaming
    public string? EndpointID { get; set; }

    /// <summary>MAC address assigned to the container's interface on this network.</summary>
    public string? MacAddress { get; set; }

    /// <summary>IPv4 address (with CIDR prefix) assigned to the container on this network.</summary>
    // ReSharper disable once InconsistentNaming
    public string? IPv4Address { get; set; }

    /// <summary>IPv6 address (with CIDR prefix) assigned to the container on this network, if any.</summary>
    // ReSharper disable once InconsistentNaming
    public string? IPv6Address { get; set; }
  }
}
