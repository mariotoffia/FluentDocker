using System.Collections.Generic;

namespace FluentDocker.Model.Networks
{
  public sealed class Ipam
  {
    public string Driver { get; set; }

    public IDictionary<string, string> Options { get; set; }

    public IList<IpamConfig> Config { get; set; }
  }
}
