#nullable enable
using System.Collections.Generic;

namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// One named compose config item.
  /// </summary>
  /// <remarks>
  /// Note: config definitions are only supported in version 3.3 and higher of the compose file format.
  /// </remarks>
  public sealed class ConfigurationItemDefinition
  {
    /// <summary>The config name, matching its key in the owning <see cref="ConfigurationDefinition.Items"/> map.</summary>
    public string? Name { get; set; }
    /// <summary>The config's attributes (e.g. <c>file</c>, <c>external</c>, <c>name</c>) as raw key-value pairs.</summary>
    public IDictionary<string, string> NameValues { get; set; } = new Dictionary<string, string>();
  }
}
