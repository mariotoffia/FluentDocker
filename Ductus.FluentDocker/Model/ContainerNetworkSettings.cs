using System.Collections.Generic;

// ReSharper disable InconsistentNaming
namespace Ductus.FluentDocker.Model
{
  public sealed class ContainerNetworkSettings
  {
    public string Bridge { get; set; }
    public string SandboxID { get; set; }
    public bool HairpinMode { get; set; }
    public string LinkLocalIPv6Address { get; set; }
    public string LinkLocalIPv6PrefixLen { get; set; }
    public string SandboxKey { get; set; }
    public string SecondaryIPAddresses { get; set; }
    public string SecondaryIPv6Addresses { get; set; }
    public string EndpointID { get; set; }
    public string Gateway { get; set; }
    public string GlobalIPv6Address { get; set; }
    public string GlobalIPv6PrefixLen { get; set; }
    public string IPAddress { get; set; }
    public string IPPrefixLen { get; set; }
    public string IPv6Gateway { get; set; }
    public string MacAddress { get; set; }
    public Dictionary<string, HostIpEndpoint[]> Ports { get; set; }
    public Dictionary<string, BridgeNetwork> Networks { get; set; }
  }
}