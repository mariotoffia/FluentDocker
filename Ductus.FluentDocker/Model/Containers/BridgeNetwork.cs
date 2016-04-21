// ReSharper disable InconsistentNaming

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class BridgeNetwork
  {
    public string NetworkID { get; set; }
    public string EndpointID { get; set; }
    public string Gateway { get; set; }
    public string IPAddress { get; set; }
    public int IPPrefixLen { get; set; }
    public string IPv6Gateway { get; set; }
    public string GlobalIPv6Address { get; set; }
    public int GlobalIPv6PrefixLen { get; set; }
    public string MacAddress { get; set; }
  }
}