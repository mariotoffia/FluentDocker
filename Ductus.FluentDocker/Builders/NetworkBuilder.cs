using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class NetworkBuilder : BaseBuilder<INetworkService>
  {
    private readonly NetworkCreateParams _config = new NetworkCreateParams();
    private string _name;
    private bool _removeOnDispose = true;
    private bool _reuseIfExist;

    public NetworkBuilder(IBuilder parent, string name = null) : base(parent)
    {
      _name = name;
    }

    public override INetworkService Build()
    {
      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build network {_name} since no host service is defined");

      if (_reuseIfExist)
      {
        var network = host.Value.GetNetworks().FirstOrDefault(x => x.Name == _name);
        if (null != network) return network;
      }

      return host.Value.CreateNetwork(_name, _config, _removeOnDispose);
    }

    public ImageBuilder DefineImage(string image = null)
    {
      var builder = new ImageBuilder(this).AsImageName(image);
      Childs.Add(builder);
      return builder;
    }

    public ContainerBuilder UseContainer()
    {
      var builder = new ContainerBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public NetworkBuilder WithName(string name)
    {
      _name = name;
      return this;
    }

    public NetworkBuilder ReuseIfExist()
    {
      _reuseIfExist = true;
      return this;
    }

    [Deprecated("Wrong Spelling, replaced with KeepOnDispose", "3.0.0")]
    public NetworkBuilder KeepOnDispse()
    {
      _removeOnDispose = false;
      return this;
    }

    public NetworkBuilder KeepOnDispose()
    {
      _removeOnDispose = false;

      return this;
    }

    public NetworkBuilder UseAuxAddress(string name, string ip)
    {
      if (null == _config.AuxAddress) _config.AuxAddress = new Dictionary<string, string>();

      _config.AuxAddress[name] = ip;
      return this;
    }

    public NetworkBuilder UseDriver(string driverName)
    {
      _config.Driver = driverName;
      return this;
    }

    public NetworkBuilder UseDriverOption(string name, string value)
    {
      if (null == _config.DriverOptions) _config.DriverOptions = new Dictionary<string, string>();

      _config.DriverOptions[name] = value;
      return this;
    }

    public NetworkBuilder UseGateway(params string[] gateway)
    {
      _config.Gateway = gateway;
      return this;
    }

    public NetworkBuilder IsInternal()
    {
      _config.Internal = true;
      return this;
    }

    /// <summary>
    ///   Enables manual container attachment to the network.
    /// </summary>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   By default manual attachment of containers is set to false.
    ///   This is vital if e.g. overlay network with internal is specified.
    /// </remarks>
    public NetworkBuilder IsAttachAble()
    {
      _config.Attachable = true;
      return this;
    }

    public NetworkBuilder UseIpRange(params string[] iprange)
    {
      _config.IpRange = iprange;
      return this;
    }

    public NetworkBuilder UseIpamOption(string name, string value)
    {
      if (null == _config.IpamOptions) _config.IpamOptions = new Dictionary<string, string>();

      _config.IpamOptions[name] = value;
      return this;
    }

    public NetworkBuilder EnableIpV6()
    {
      _config.EnableIpV6 = true;
      return this;
    }

    public NetworkBuilder UseLabel(params string[] labels)
    {
      _config.Labels = labels;
      return this;
    }

    public NetworkBuilder UseSubnet(params string[] subnets)
    {
      _config.Subnet = subnets;
      return this;
    }

    protected override IBuilder InternalCreate()
    {
      return new NetworkBuilder(this);
    }
  }
}