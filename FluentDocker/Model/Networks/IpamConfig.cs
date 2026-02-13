using System.Collections.Generic;

namespace FluentDocker.Model.Networks
{
  public sealed class IpamConfig
  {
    public string Subnet { get; set; }
    public string Gateway { get; set; }
    public string IPRange { get; set; }
    public IDictionary<string, string> AuxiliaryAddresses { get; set; }
  }
}
