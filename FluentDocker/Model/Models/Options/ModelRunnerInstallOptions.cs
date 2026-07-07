#nullable enable
namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Options for installing the runner on Docker Engine CE
  /// (maps to <c>docker model install-runner</c>).
  /// </summary>
  public sealed class ModelRunnerInstallOptions
  {
    /// <summary>GPU selection (<c>--gpu auto|cuda|none</c>).</summary>
    public string? Gpu { get; init; }
  }
}
