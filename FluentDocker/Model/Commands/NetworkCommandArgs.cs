using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker network connect command.
  /// </summary>
  public struct NetworkConnectCommandArgs
  {
    /// <summary>The network name or ID.</summary>
    public string Network { get; set; }
    /// <summary>The container name or ID.</summary>
    public string Container { get; set; }
    /// <summary>Add network-scoped alias for the container.</summary>
    public string[] Aliases { get; set; }
    /// <summary>Driver options for the network.</summary>
    public string[] DriverOpts { get; set; }
    /// <summary>IPv4 address for the container.</summary>
    public string Ipv4Address { get; set; }
    /// <summary>IPv6 address for the container.</summary>
    public string Ipv6Address { get; set; }
    /// <summary>Add link to another container.</summary>
    public string[] Links { get; set; }
    /// <summary>Add a link-local address for the container.</summary>
    public string[] LinkLocalIps { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--alias=", Aliases);
      sb.OptionIfExists("--driver-opt=", DriverOpts);
      sb.OptionIfExists("--ip ", Ipv4Address);
      sb.OptionIfExists("--ip6 ", Ipv6Address);
      sb.OptionIfExists("--link=", Links);
      sb.OptionIfExists("--link-local-ip=", LinkLocalIps);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker network disconnect command.
  /// </summary>
  public struct NetworkDisconnectCommandArgs
  {
    /// <summary>The network name or ID.</summary>
    public string Network { get; set; }
    /// <summary>The container name or ID.</summary>
    public string Container { get; set; }
    /// <summary>Force the container to disconnect from a network.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" -f");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker network ls command.
  /// </summary>
  public struct NetworkLsCommandArgs
  {
    /// <summary>Provide filter values.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Do not truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Only display network IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);
      if (NoTrunc)
        sb.Append(" --no-trunc");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker network rm command.
  /// </summary>
  public struct NetworkRmCommandArgs
  {
    /// <summary>Network names or IDs to remove.</summary>
    public IList<string> Networks { get; set; }
    /// <summary>Do not error if the network does not exist.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" -f");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker network inspect command.
  /// </summary>
  public struct NetworkInspectCommandArgs
  {
    /// <summary>Network names or IDs to inspect.</summary>
    public IList<string> Networks { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Verbose output for diagnostics.</summary>
    public bool Verbose { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (Verbose)
        sb.Append(" --verbose");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker network create command.
  /// </summary>
  public struct NetworkCreateCommandArgs
  {
    /// <summary>The network name.</summary>
    public string Name { get; set; }
    /// <summary>Enable manual container attachment.</summary>
    public bool Attachable { get; set; }
    /// <summary>Auxiliary IPv4 or IPv6 addresses used by Network driver.</summary>
    public IDictionary<string, string> AuxAddress { get; set; }
    /// <summary>The network driver.</summary>
    public string Driver { get; set; }
    /// <summary>IPv4 or IPv6 Gateway for the master subnet.</summary>
    public string[] Gateway { get; set; }
    /// <summary>Create swarm routing-mesh network.</summary>
    public bool Ingress { get; set; }
    /// <summary>Restrict external access to the network.</summary>
    public bool Internal { get; set; }
    /// <summary>Allocate container IP from a sub-range.</summary>
    public string[] IpRange { get; set; }
    /// <summary>IP Address Management Driver.</summary>
    public string IpamDriver { get; set; }
    /// <summary>Set IPAM driver specific options.</summary>
    public IDictionary<string, string> IpamOpts { get; set; }
    /// <summary>Enable IPv6 networking.</summary>
    public bool Ipv6 { get; set; }
    /// <summary>Set metadata on a network.</summary>
    public string[] Labels { get; set; }
    /// <summary>Set driver specific options.</summary>
    public IDictionary<string, string> Opts { get; set; }
    /// <summary>Control the network's scope.</summary>
    public string Scope { get; set; }
    /// <summary>Subnet in CIDR format.</summary>
    public string[] Subnet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Attachable)
        sb.Append(" --attachable");
      sb.OptionIfExists("--aux-address=", AuxAddress);
      sb.OptionIfExists("--driver=", Driver);
      sb.OptionIfExists("--gateway=", Gateway);
      if (Ingress)
        sb.Append(" --ingress");
      if (Internal)
        sb.Append(" --internal");
      sb.OptionIfExists("--ip-range=", IpRange);
      sb.OptionIfExists("--ipam-driver=", IpamDriver);
      sb.OptionIfExists("--ipam-opt=", IpamOpts);
      if (Ipv6)
        sb.Append(" --ipv6");
      sb.OptionIfExists("--label=", Labels);
      sb.OptionIfExists("--opt=", Opts);
      sb.OptionIfExists("--scope=", Scope);
      sb.OptionIfExists("--subnet=", Subnet);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker network prune command.
  /// </summary>
  public struct NetworkPruneCommandArgs
  {
    /// <summary>Provide filter values.</summary>
    public string[] Filters { get; set; }
    /// <summary>Do not prompt for confirmation.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--filter ", Filters);
      if (Force)
        sb.Append(" --force");

      return sb.ToString();
    }
  }
}

