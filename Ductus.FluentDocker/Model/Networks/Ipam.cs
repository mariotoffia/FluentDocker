using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Networks
{
  public sealed class Ipam
  {
    public string Driver { get; set; }

    //TODO: "Options": null
    public IList<IpamConfig> Config { get; set; }
  }
}