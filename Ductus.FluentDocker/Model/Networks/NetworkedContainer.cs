namespace Ductus.FluentDocker.Model.Networks
{
  public sealed class NetworkedContainer
  {
    public string Name { get; set; }

    // ReSharper disable once InconsistentNaming
    public string EndpointID { get; set; }

    public string MacAddress { get; set; }

    // ReSharper disable once InconsistentNaming
    public string IPv4Address { get; set; }

    // ReSharper disable once InconsistentNaming
    public string IPv6Address { get; set; }
  }
}