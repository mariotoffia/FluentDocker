#nullable enable
using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Options for loading / running a model. The fields split into two groups by where the
  /// Docker Model Runner CLI surfaces them:
  /// <list type="bullet">
  /// <item><description>
  /// <b>Run flags</b> (<see cref="Detach"/>, <see cref="Debug"/>, <see cref="OpenAiUrl"/>,
  /// <see cref="WebSearch"/>) map directly onto <c>docker model run</c>.
  /// </description></item>
  /// <item><description>
  /// <b>Configure-only</b> settings (<see cref="ContextSize"/>, <see cref="RuntimeFlags"/>)
  /// have NO <c>run</c> flag in DMR v1.2.1; <c>LoadAsync</c> applies them via
  /// <c>docker model configure</c> just BEFORE the run so they are not silently dropped.
  /// </description></item>
  /// </list>
  /// Backend selection is implicit (chosen from the model format) and is not a <c>run</c>
  /// flag, so it is not modeled here (see <see cref="ModelConfigureOptions.Backend"/>).
  /// </summary>
  public sealed class ModelRunOptions
  {
    /// <summary>
    /// Load detached (<c>-d</c>/<c>--detach</c>) so the call returns once the model is
    /// resident rather than streaming a chat session. Defaults to <c>true</c> — loading a
    /// model for later inference is the library's use case.
    /// </summary>
    public bool Detach { get; init; } = true;

    /// <summary>Enable engine debug output (<c>--debug</c>).</summary>
    public bool Debug { get; init; }

    /// <summary>
    /// Override the OpenAI-compatible base URL the run session targets
    /// (<c>--openaiurl</c>). <c>null</c>/empty emits nothing.
    /// </summary>
    public string? OpenAiUrl { get; init; }

    /// <summary>Enable the web-search tool for the session (<c>--websearch</c>).</summary>
    public bool WebSearch { get; init; }

    /// <summary>
    /// Configure-only: persistent context window applied via <c>docker model configure
    /// --context-size N</c> BEFORE the run (there is no <c>run</c> flag for it). <c>null</c>
    /// leaves it unchanged.
    /// </summary>
    private int? _contextSize;

    public int? ContextSize
    {
      get => _contextSize;
      init
      {
        if (value is <= 0)
          throw new ArgumentOutOfRangeException(nameof(ContextSize), value, "Context size must be positive.");
        _contextSize = value;
      }
    }

    /// <summary>
    /// Configure-only: raw inference-engine flags applied via the <c>docker model configure
    /// -- …</c> passthrough BEFORE the run (there is no <c>run</c> flag for them). See
    /// <see cref="LlamaCppRuntimeFlags"/> for a typed builder.
    /// </summary>
    public IReadOnlyList<string>? RuntimeFlags { get; init; }
  }
}
