#nullable enable
using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// Local-store metadata for a model (the result of <c>docker model inspect</c>
  /// or <c>docker model ls</c>), mapped from DMR CLI/HTTP output.
  /// </summary>
  public sealed class ModelInfo
  {
    /// <summary>The model image id / digest (e.g. <c>sha256:…</c>).</summary>
    public string? Id { get; init; }

    /// <summary>The canonical model reference.</summary>
    public ModelReference? Reference { get; init; }

    /// <summary>All tags associated with the model.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The artifact format, e.g. <c>gguf</c> or <c>safetensors</c>.</summary>
    public string? Format { get; init; }

    /// <summary>The model architecture, e.g. <c>llama</c>.</summary>
    public string? Architecture { get; init; }

    /// <summary>The parameter count, e.g. <c>7B</c> / <c>361.82 M</c>.</summary>
    public string? ParameterCount { get; init; }

    /// <summary>The quantization scheme, e.g. <c>Q4_K_M</c>.</summary>
    public string? Quantization { get; init; }

    /// <summary>The on-disk size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The creation timestamp.</summary>
    public DateTime Created { get; init; }

    /// <summary>Effective configuration (context size, etc.).</summary>
    public IReadOnlyDictionary<string, string>? Config { get; init; }
  }
}
