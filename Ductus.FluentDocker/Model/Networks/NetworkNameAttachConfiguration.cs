using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Networks
{
  public sealed class NetworkNameAttachConfiguration
  {
    public string Alias { get; set; }
    public string NetworkName { get; set; }
  }
}
