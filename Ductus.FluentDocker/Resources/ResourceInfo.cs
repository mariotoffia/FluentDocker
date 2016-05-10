using System.Reflection;

namespace Ductus.FluentDocker.Resources
{
  public sealed class ResourceInfo
  {
    public string Resource { get; set; }
    public string Namespace { get; set; }
    public string RelativeNamespace { get; set; }
    public Assembly Assembly { get; set; }
  }
}
