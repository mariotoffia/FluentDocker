using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Networks
{
  public sealed class NetworkAttachConfiguration
  {
    public string Alias { get; set; }
    public INetworkService Network { get; set; }
  }
}
