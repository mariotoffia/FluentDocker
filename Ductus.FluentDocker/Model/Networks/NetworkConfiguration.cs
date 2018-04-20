using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Networks
{
  public sealed class NetworkConfiguration
  {
    public string Name { get; set; }
    public string Id { get; set; }
    public DateTime Created { get; set; }
    public string Scope { get; set; }
    public string Driver { get; set; }
    public bool EnableIPv6 { get; set; }
    public bool Internal { get; set; }
    public bool Attachable { get; set; }
    public bool Ingress { get; set; }
    public bool ConfigOnly { get; set; }

    // ReSharper disable once InconsistentNaming
    public Ipam IPAM { get; set; }

    public IDictionary<string, string> ConfigFrom { get; set; }
    public IDictionary<string, NetworkedContainer> Containers { get; set; }

    public IDictionary<string, string> Options { get; set; }
    //TODO: "Labels": {}
  }
}