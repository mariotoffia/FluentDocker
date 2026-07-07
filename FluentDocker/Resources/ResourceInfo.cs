#nullable enable
using System.Reflection;

namespace FluentDocker.Resources
{
  public sealed class ResourceInfo
  {
    public string Resource { get; set; } = null!;
    public string Namespace { get; set; } = null!;
    public string? Root { get; set; }
    public string RelativeRootNamespace { get; set; } = null!;
    public Assembly Assembly { get; set; } = null!;
  }
}
