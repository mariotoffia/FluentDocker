namespace Ductus.FluentDocker.Model.Networks
{
  public enum NetworkType
  {
    Unknown = 0,
    Bridge,
    Host,
    Overlay,
    Ipvlan,
    Macvlan,
    None,
    Custom
  }
}
