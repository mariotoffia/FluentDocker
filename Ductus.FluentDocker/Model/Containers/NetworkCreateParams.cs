using System.Collections.Generic;
using System.Text;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class NetworkCreateParams
  {
    /// <summary>
    /// Enables manual container attachment (disabled by default)
    /// </summary>
    /// <remarks>
    ///  --attachable
    /// </remarks>
    public bool Attachable { get; set; }
    
    /// <summary>
    ///   Auxiliary ipv4 or ipv6 addresses used by Network driver
    /// </summary>
    /// <remarks>
    ///   --aux-address=map[]
    /// </remarks>
    public IDictionary<string, string> AuxAddress { get; set; }

    /// <summary>
    ///   Driver to manage the Network
    /// </summary>
    /// <remarks>
    ///   -d, --driver=bridge
    /// </remarks>
    public string Driver { get; set; } = "bridge";

    /// <summary>
    ///   Set driver specific options
    /// </summary>
    /// <remarks>
    ///   -o, --opt=map[]
    /// </remarks>
    public IDictionary<string, string> DriverOptions { get; set; }

    /// <summary>
    ///   ipv4 or ipv6 Gateway for the master subnet
    /// </summary>
    /// <remarks>
    ///   --gateway=[]
    /// </remarks>
    public string[] Gateway { get; set; }

    /// <summary>
    ///   Restricts external access to the network
    /// </summary>
    /// <remarks>
    ///   --internal
    /// </remarks>
    public bool Internal { get; set; }

    /// <summary>
    ///   Allocate container ip from a sub-range
    /// </summary>
    /// <remarks>
    ///   --ip-range=[]
    /// </remarks>
    public string[] IpRange { get; set; }

    /// <summary>
    ///   IP Address Management Driver
    /// </summary>
    /// <remarks>
    ///   --ipam-driver=default
    /// </remarks>
    public string IpamDriver { get; set; } = "default";

    /// <summary>
    ///   Set IPAM driver specific options
    /// </summary>
    /// <remarks>
    ///   --ipam-opt=map[]
    /// </remarks>
    public IDictionary<string, string> IpamOptions { get; set; }

    /// <summary>
    ///   Enable IPv6 networking
    /// </summary>
    /// <remarks>
    ///   --ipv6
    /// </remarks>
    public bool EnableIpV6 { get; set; }

    /// <summary>
    ///   Set metadata on a network
    /// </summary>
    /// <remarks>
    ///   --label=[]
    /// </remarks>
    public string[] Labels { get; set; }

    /// <summary>
    ///   Subnet in CIDR format that represents a network segment
    /// </summary>
    /// <remarks>
    ///   --subnet=[]
    /// </remarks>
    public string[] Subnet { get; set; }

    public override string ToString()
    {
      return new StringBuilder()
        .OptionIfExists("--attachable", Attachable)
        .OptionIfExists("--aux-address=", AuxAddress)
        .OptionIfExists("--driver=", Driver)
        .OptionIfExists("--opt=", DriverOptions)
        .OptionIfExists("--gateway=", Gateway)
        .OptionIfExists("--internal", Internal)
        .OptionIfExists("--ip-range=", IpRange)
        .OptionIfExists("--ipam-driver=", IpamDriver)
        .OptionIfExists("--ipam-opt=", IpamOptions)
        .OptionIfExists("--ipv6", EnableIpV6)
        .OptionIfExists("--label=", Labels)
        .OptionIfExists("--subnet=", Subnet)
        .ToString();
    }
  }
}
