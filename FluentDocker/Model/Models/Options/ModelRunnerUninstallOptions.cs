#nullable enable
namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Options for uninstalling the runner on Docker Engine CE
  /// (maps to <c>docker model uninstall-runner</c>); used for upgrades.
  /// </summary>
  public sealed class ModelRunnerUninstallOptions
  {
    /// <summary>Also remove runner images (<c>--images</c>).</summary>
    public bool RemoveImages { get; init; }

    /// <summary>Also remove local models (<c>--models</c>).</summary>
    public bool RemoveModels { get; init; }
  }
}
