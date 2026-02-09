namespace FluentDocker.Model.Networks
{
  public sealed class IpamConfig
  {
    public string Subnet { get; set; }
    public string Gateway { get; set; }
  }
}
