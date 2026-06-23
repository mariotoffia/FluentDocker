namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Options for loading / running a model (maps to <c>docker model run</c> flags).
  /// </summary>
  public sealed class ModelRunOptions
  {
    /// <summary>Run detached (<c>-d</c>) — load and keep resident.</summary>
    public bool Detach { get; init; }

    /// <summary>Skip the host runtime-memory check (<c>--ignore-runtime-memory-check</c>).</summary>
    public bool IgnoreRuntimeMemoryCheck { get; init; }

    /// <summary>Override the inference backend for this run.</summary>
    public string Backend { get; init; }

    /// <summary>Enable engine debug output (<c>--debug</c>).</summary>
    public bool Debug { get; init; }
  }
}
