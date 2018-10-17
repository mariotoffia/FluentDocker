using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   Networks to join, referencing entries under the top-level networks key.
  /// </summary>
  /// <remarks>
  ///   The general format is shown here.
  ///   services:
  ///   some-service:
  ///   networks:
  ///   some-network:
  ///   aliases:
  ///   - alias1
  ///   - alias3
  ///   other-network:
  ///   aliases:
  ///   - alias2
  /// An example for static addressing:
  /// version: '2.1'
  /// 
  /// services:
  /// app:
  /// image: busybox
  /// command: ifconfig
  /// networks:
  /// app_net:
  /// ipv4_address: 172.16.238.10
  /// ipv6_address: 2001:3984:3989::10
  /// 
  /// networks:
  /// app_net:
  /// driver: bridge
  /// enable_ipv6: true
  /// ipam:
  /// driver: default
  /// config:
  /// -
  /// subnet: 172.16.238.0/24
  /// -
  /// subnet: 2001:3984:3989::/64
  /// </remarks>
  public sealed class ServiceNetworkDefinition
  {
    /// <summary>
    ///   Name of the network.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///   Aliases (alternative hostnames) for this service on the network.
    /// </summary>
    /// <remarks>
    ///   Other containers on the same network can use either the service name or this alias to connect to one of the
    ///   service’s containers. Since aliases is network-scoped, the same service can have different aliases on different
    ///   networks. Note: A network-wide alias can be shared by multiple containers, and even by multiple services.
    ///   If it is, then exactly which container the name resolves to is not guaranteed.
    /// </remarks>
    public IList<string> Aliases { get; set; } = new List<string>();

    /// <summary>
    ///   Specify a static IP address for containers for this service when joining the network.
    /// </summary>
    /// <remarks>
    ///   Note: This option do not currently work in swarm mode.
    ///   The corresponding network configuration in the top-level networks section must have an ipam block with subnet
    ///   configurations covering each static address.
    /// </remarks>
    public string IpV4Address { get; set; }

    /// <summary>
    ///   Specify a static IP address for containers for this service when joining the network.
    /// </summary>
    /// <remarks>
    ///   Note: This is a compose 2.0 file feature. This option do not currently work in swarm mode.
    ///   The corresponding network configuration in the top-level networks section must have an ipam block with subnet
    ///   configurations covering each static address. Since IPv6 addressing is desired, the enable_ipv6 option must be
    ///   set.
    /// </remarks>
    public string IpV6Address { get; set; }
  }
}