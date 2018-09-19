using System;

namespace Ductus.FluentDocker.Common
{
  /// <summary>
  /// Specifies that the implementation is experimental and is subject to change.
  /// </summary>
  [AttributeUsage(AttributeTargets.All)]
  public sealed class ExperimentalAttribute : Attribute
  {
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