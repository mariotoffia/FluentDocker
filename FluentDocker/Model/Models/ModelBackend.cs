using System;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// An open value identifying an inference backend (engine). Modeled as an
  /// extensible value rather than a closed enum so future backends do not break
  /// callers. The default value (<see cref="IsDefault"/>) means "leave unset"
  /// (do not emit a <c>--backend</c> flag).
  /// </summary>
  public readonly struct ModelBackend : IEquatable<ModelBackend>
  {
    /// <summary>The default llama.cpp backend.</summary>
    public static readonly ModelBackend LlamaCpp = new("llama.cpp", "gguf");

    /// <summary>The vLLM backend (NVIDIA only).</summary>
    public static readonly ModelBackend Vllm = new("vllm", "safetensors");

    /// <summary>The Diffusers (image generation) backend (NVIDIA only).</summary>
    public static readonly ModelBackend Diffusers = new("diffusers", "dduf");

    /// <summary>
    /// Creates a backend value.
    /// </summary>
    /// <param name="name">The backend name (e.g. <c>llama.cpp</c>).</param>
    /// <param name="format">An optional model-format hint (diagnostics only).</param>
    public ModelBackend(string name, string format = null)
    {
      Name = name;
      Format = format;
    }

    /// <summary>The backend name, or <c>null</c> for the default ("leave unset").</summary>
    public string Name { get; }

    /// <summary>
    /// An optional model-format hint (<c>gguf</c>, <c>safetensors</c>, <c>dduf</c>); diagnostics only.
    /// Explicitly NOT part of value identity — two backends with the same
    /// <see cref="Name"/> but different formats are equal.
    /// </summary>
    public string Format { get; }

    /// <summary>True when no backend is set (the <c>--backend</c> flag is omitted).</summary>
    public bool IsDefault => Name is null;

    /// <summary>Creates a custom backend value.</summary>
    /// <param name="name">The backend name.</param>
    /// <returns>A <see cref="ModelBackend"/> with the given name.</returns>
    public static ModelBackend Custom(string name) => new(name);

    /// <inheritdoc />
    public bool Equals(ModelBackend other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is ModelBackend other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

    /// <summary>Value equality operator.</summary>
    public static bool operator ==(ModelBackend left, ModelBackend right) => left.Equals(right);

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(ModelBackend left, ModelBackend right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
  }
}
