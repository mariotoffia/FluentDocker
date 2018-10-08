using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// 
  /// </summary>
  /// <remarks>
  /// Note: config definitions are only supported in version 3.3 and higher of the compose file format.
  /// </remarks>
  public sealed class ConfigurationDefinition
  {
    public IDictionary<string, ConfigurationItemDefinition> Items { get; set; } =
      new Dictionary<string, ConfigurationItemDefinition>();
  }
}