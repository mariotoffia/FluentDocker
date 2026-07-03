using System.Collections.Generic;

// ReSharper disable InconsistentNaming

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Network settings section of container inspect output.
  /// </summary>
  public sealed class ContainerNetworkSettings
  {
    /// <summary>Default bridge network name.</summary>
    public string Bridge { get; set; }

    /// <summary>Sandbox ID assigned by the runtime.</summary>
    public string SandboxID { get; set; }

    /// <summary>Whether hairpin mode is enabled.</summary>
    public bool HairpinMode { get; set; }

    /// <summary>Link-local IPv6 address.</summary>
    public string LinkLocalIPv6Address { get; set; }

    /// <summary>Link-local IPv6 prefix length.</summary>
    public string LinkLocalIPv6PrefixLen { get; set; }

    /// <summary>Path to the network namespace sandbox key.</summary>
    public string SandboxKey { get; set; }

    /// <summary>Secondary IPv4 addresses as emitted by inspect.</summary>
    public string SecondaryIPAddresses { get; set; }

    /// <summary>Secondary IPv6 addresses as emitted by inspect.</summary>
    public string SecondaryIPv6Addresses { get; set; }

    /// <summary>Endpoint ID on the default network.</summary>
    public string EndpointID { get; set; }

    /// <summary>IPv4 gateway.</summary>
    public string Gateway { get; set; }

    /// <summary>Global IPv6 address.</summary>
    public string GlobalIPv6Address { get; set; }

    /// <summary>Global IPv6 prefix length.</summary>
    public string GlobalIPv6PrefixLen { get; set; }

    /// <summary>IPv4 address.</summary>
    public string IPAddress { get; set; }

    /// <summary>IPv4 prefix length.</summary>
    public string IPPrefixLen { get; set; }

    /// <summary>IPv6 gateway.</summary>
    public string IPv6Gateway { get; set; }

    /// <summary>MAC address.</summary>
    public string MacAddress { get; set; }

    /// <summary>Published ports keyed by container port/protocol.</summary>
    public Dictionary<string, HostIpEndpoint[]> Ports { get; set; }

    /// <summary>Per-network endpoint settings keyed by network name.</summary>
    public Dictionary<string, BridgeNetwork> Networks { get; set; }
  }
}
