#nullable enable
using System.Collections.Generic;

namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// Top-level compose config definitions.
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
