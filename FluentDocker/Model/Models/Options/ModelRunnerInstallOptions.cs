#nullable enable
using System;

namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Options for installing the runner on Docker Engine CE
  /// (maps to <c>docker model install-runner</c>).
  /// </summary>
  public sealed class ModelRunnerInstallOptions
  {
    private string? _gpu;

    /// <summary>GPU selection (<c>--gpu auto|cuda|none</c>).</summary>
    public string? Gpu
    {
      get => _gpu;
      init
      {
        if (value != null &&
            !string.Equals(value, "auto", StringComparison.Ordinal) &&
            !string.Equals(value, "cuda", StringComparison.Ordinal) &&
            !string.Equals(value, "none", StringComparison.Ordinal))
          throw new ArgumentOutOfRangeException(nameof(Gpu), value, "--gpu must be one of auto|cuda|none.");
        _gpu = value;
      }
    }
  }
}
