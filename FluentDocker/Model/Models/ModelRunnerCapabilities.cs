using System.Collections.Generic;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// Feature-detection surface so callers (and tests) can branch on what the
  /// backing runner implementation actually supports.
  /// </summary>
  public sealed class ModelRunnerCapabilities
  {
    /// <summary>Supports model distribution / store operations (pull/ls/rm/…).</summary>
    public bool SupportsManagement { get; init; }

    /// <summary>Supports runtime control (ps/configure/unload/…).</summary>
    public bool SupportsRuntimeControl { get; init; }

    /// <summary>Supports inference (chat/completion/embed).</summary>
    public bool SupportsInference { get; init; }

    /// <summary>Supports streaming inference (SSE).</summary>
    public bool SupportsStreaming { get; init; }

    /// <summary>Supports embeddings.</summary>
    public bool SupportsEmbeddings { get; init; }

    /// <summary>Supports packaging (package/tag/push).</summary>
    public bool SupportsPackaging { get; init; }

    /// <summary>The default backend, e.g. <c>llama.cpp</c>.</summary>
    public string DefaultBackend { get; init; }

    /// <summary>The backends the runner reports as available.</summary>
    public IReadOnlyList<string> AvailableBackends { get; init; }
  }
}
