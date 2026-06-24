namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Options for loading / running a model (maps to <c>docker model run</c> flags).
  /// Only flags exposed by the current Docker Model Runner CLI are modeled — backend
  /// selection is implicit (chosen from the model format) and is not a <c>run</c> flag.
  /// </summary>
  public sealed class ModelRunOptions
  {
    /// <summary>Run detached (<c>-d</c>) — load and keep resident.</summary>
    public bool Detach { get; init; }

    /// <summary>Enable engine debug output (<c>--debug</c>).</summary>
    public bool Debug { get; init; }
  }
}
