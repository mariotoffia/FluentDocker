#nullable enable
using System;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// The raw, verbatim model id sent in the OpenAI-compatible inference body
  /// (the <c>model</c> field of chat/completion/embeddings requests). (Preview)
  /// </summary>
  /// <remarks>
  /// Unlike <see cref="ModelReference"/> — which is a Docker <em>artifact</em>
  /// reference and defaults a missing tag to <c>:latest</c> — an
  /// <see cref="InferenceModelId"/> carries the model id <strong>exactly</strong> as
  /// supplied. No tag is injected and no transformation is applied, so a remote
  /// OpenAI id like <c>gpt-4o-mini</c> is sent verbatim (never <c>gpt-4o-mini:latest</c>,
  /// which a real OpenAI-compatible endpoint would reject).
  /// <para>
  /// When the source is a local Docker model the id is derived from its
  /// <see cref="ModelReference"/> via <see cref="FromModelReference"/>, which drops a
  /// <c>:latest</c> tag (so <c>ai/smollm2</c> infers as <c>ai/smollm2</c>). A
  /// <see cref="ModelReference"/> cannot distinguish an explicit <c>:latest</c> from the
  /// defaulted tag after parsing, so both render as the bare inference id; non-latest tags and
  /// digests are preserved.
  /// </para>
  /// </remarks>
  public readonly struct InferenceModelId : IEquatable<InferenceModelId>
  {
    private readonly string _value;

    /// <summary>
    /// Creates an inference model id from a non-empty raw string, preserved verbatim.
    /// </summary>
    /// <param name="value">The raw model id (e.g. <c>gpt-4o-mini</c>, <c>ai/smollm2</c>).</param>
    /// <exception cref="ArgumentException">The value is null, empty or whitespace.</exception>
    public InferenceModelId(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("Inference model id must not be null, empty or whitespace.", nameof(value));

      _value = value;
    }

    /// <summary>The raw model id, exactly as supplied.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Derives the inference id from a Docker artifact <see cref="ModelReference"/>,
    /// dropping a <c>:latest</c> tag (explicit <c>:latest</c> cannot be distinguished after
    /// parsing); non-latest tags and digests are kept.
    /// </summary>
    /// <param name="reference">The Docker model reference.</param>
    /// <returns>The derived inference id, or <c>null</c> when <paramref name="reference"/> is null.</returns>
    public static InferenceModelId? FromModelReference(ModelReference reference)
    {
      if (reference is null)
        return null;

      // ModelReference always materializes a tag ("latest") when neither tag nor
      // digest was supplied. That default must NOT leak into the inference body, so a
      // bare "latest"-tagged-and-undigested reference is rendered without its tag.
      // An explicitly-pinned tag/digest is part of identity and is preserved.
      if (reference.Digest == null && string.Equals(reference.Tag, "latest", StringComparison.Ordinal))
        return new InferenceModelId(StripDefaultLatest(reference));

      return new InferenceModelId(reference.ToString());
    }

    private static string StripDefaultLatest(ModelReference reference)
    {
      // Rebuild the canonical form minus the tag (registry/namespace/name only).
      var canonical = reference.ToString();
      var name = reference.Name;
      var nameTag = name + ":" + reference.Tag;

      // The tag is always the trailing "<name>:latest" segment (no digest present).
      return canonical.EndsWith(nameTag, StringComparison.Ordinal)
          ? canonical.Substring(0, canonical.Length - reference.Tag!.Length - 1)
          : canonical;
    }

    /// <inheritdoc />
    public bool Equals(InferenceModelId other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InferenceModelId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>Returns the raw model id verbatim.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Implicitly converts a raw string to an <see cref="InferenceModelId"/>.</summary>
    public static implicit operator InferenceModelId(string value)
    {
      ArgumentNullException.ThrowIfNull(value);
      return new InferenceModelId(value);
    }

    /// <summary>Explicitly extracts the raw model id string.</summary>
    public static explicit operator string(InferenceModelId id) => id.Value;

    /// <summary>Value equality operator.</summary>
    public static bool operator ==(InferenceModelId left, InferenceModelId right) => left.Equals(right);

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(InferenceModelId left, InferenceModelId right) => !left.Equals(right);
  }
}
