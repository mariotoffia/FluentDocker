#nullable enable
using System.Collections.Generic;

namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// A top-level Compose <c>models:</c> entry: an OCI/HF model reference plus
  /// optional context size and raw engine runtime flags.
  /// </summary>
  public sealed class ComposeModelSpec
  {
    /// <summary>The model key (the map key under <c>models:</c>).</summary>
    public string? Key { get; init; }

    /// <summary>The model reference (<c>ai/…</c> or <c>hf.co/…</c>).</summary>
    public string? Model { get; init; }

    /// <summary>The optional context size (<c>context_size:</c>).</summary>
    public int? ContextSize { get; init; }

    /// <summary>Raw engine runtime flags (<c>runtime_flags:</c>).</summary>
    public IReadOnlyList<string>? RuntimeFlags { get; init; }
  }

  /// <summary>
  /// A per-service Compose <c>models:</c> binding. When <see cref="EndpointVar"/> /
  /// <see cref="ModelVar"/> are null the short list form (<c>- key</c>) is used;
  /// otherwise the long map form with custom env-var names.
  /// </summary>
  public sealed class ComposeServiceModelBinding
  {
    /// <summary>The service name.</summary>
    public string? Service { get; init; }

    /// <summary>The model key referenced from the top-level <c>models:</c> map.</summary>
    public string? ModelKey { get; init; }

    /// <summary>The custom endpoint env-var name (long form), or null for the default.</summary>
    public string? EndpointVar { get; init; }

    /// <summary>The custom model-id env-var name (long form), or null for the default.</summary>
    public string? ModelVar { get; init; }

    /// <summary>True when the long (map) form with custom env-var names is used.</summary>
    public bool IsLong => EndpointVar != null || ModelVar != null;
  }
}
