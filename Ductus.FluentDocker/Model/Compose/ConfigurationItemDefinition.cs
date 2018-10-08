using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// 
  /// </summary>
  /// <remarks>
  /// Note: config definitions are only supported in version 3.3 and higher of the compose file format.
  /// </remarks>
  public sealed class ConfigurationItemDefinition
  {
    public string Name { get; set; }
    public IDictionary<string, string> NameValues { get; set; } = new Dictionary<string, string>();
  }
}