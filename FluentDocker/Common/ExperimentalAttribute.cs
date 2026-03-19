using System;

namespace FluentDocker.Common
{
  /// <summary>
  /// Specifies that the implementation is experimental and is subject to change.
  /// </summary>
  [AttributeUsage(AttributeTargets.All)]
  public sealed class ExperimentalAttribute : Attribute
  {
    /// <summary>Creates a new instance with optional documentation URL and target version.</summary>
    /// <param name="documentation">URL or description of the experimental feature documentation.</param>
    /// <param name="targetVersion">The version when this feature is expected to be stable.</param>
    public ExperimentalAttribute(string documentation = null, string targetVersion = null)
    {
      Documentation = documentation ?? string.Empty;
      TargetVersion = targetVersion ?? string.Empty;
    }

    /// <summary>
    /// Current target version when this is to be released.
    /// </summary>
    public string TargetVersion { get; set; }
    /// <summary>
    /// Optional documentation for the experimental feature.
    /// </summary>
    public string Documentation { get; set; }
  }
}
