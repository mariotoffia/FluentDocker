using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// Immutable, value-based reference to a model: optional registry, optional
  /// namespace, name, tag and optional digest. Handles Docker Hub
  /// <c>ai/qwen3:latest</c>, Hugging Face <c>hf.co/org/repo:Q4_K_M</c> and
  /// fully-qualified <c>registry.example.com:5000/ns/name:tag</c> forms, plus
  /// <c>@sha256:…</c> digests.
  /// </summary>
  /// <remarks>
  /// Registry detection mirrors Docker's own heuristic: the first path segment is
  /// treated as a registry only when it contains a <c>.</c>, a <c>:</c> (port) or
  /// equals <c>localhost</c>. The tag defaults to <c>latest</c> unless a digest is
  /// supplied (a digest pins the artifact, so no default tag is added).
  /// Serializes transparently as its canonical string form.
  /// <para>
  /// Identity rule: all five components (registry, namespace, name, tag, digest)
  /// participate in equality. <see cref="Tag"/> therefore remains part of identity
  /// even when a <see cref="Digest"/> is present, so a tagged-and-pinned reference
  /// is not considered equal to a bare digest reference.
  /// </para>
  /// </remarks>
  [JsonConverter(typeof(ModelReferenceJsonConverter))]
  public sealed class ModelReference : IEquatable<ModelReference>
  {
    private const string DockerHubRegistryPrefix = "docker.io/";
    private string _string;

    private ModelReference(string registry, string ns, string name, string tag, string digest)
    {
      // Docker registry hosts are case-insensitive; normalize to lowercase so that
      // Equals-equal references (which compare the registry OrdinalIgnoreCase) always
      // render an identical canonical ToString(). Only the host is normalized -
      // namespace/name/tag/digest stay verbatim.
      Registry = registry?.ToLowerInvariant();
      Namespace = ns;
      Name = name;
      Tag = tag;
      Digest = digest;
    }

    /// <summary>The registry host (e.g. <c>hf.co</c>, <c>registry.io:5000</c>) or <c>null</c> for the default Docker Hub.</summary>
    public string Registry { get; }

    /// <summary>The namespace / organization (e.g. <c>ai</c>, <c>bartowski</c>) or <c>null</c> when the reference is a bare name.</summary>
    public string Namespace { get; }

    /// <summary>The model name (e.g. <c>qwen3</c>).</summary>
    public string Name { get; }

    /// <summary>The tag (e.g. <c>latest</c>, <c>Q4_K_M</c>) or <c>null</c> when a <see cref="Digest"/> is supplied.</summary>
    public string Tag { get; }

    /// <summary>The optional digest (e.g. <c>sha256:…</c>) or <c>null</c>.</summary>
    public string Digest { get; }

    /// <summary>True when the reference targets the Hugging Face registry (<c>hf.co</c>).</summary>
    public bool IsHuggingFace =>
        string.Equals(Registry, "hf.co", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a model reference string. Throws on invalid input.
    /// </summary>
    /// <param name="reference">The reference string, e.g. <c>ai/qwen3:latest</c>.</param>
    /// <returns>The parsed <see cref="ModelReference"/>.</returns>
    /// <exception cref="ArgumentException">The reference is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The reference is structurally malformed.</exception>
    public static ModelReference Parse(string reference)
    {
      if (string.IsNullOrWhiteSpace(reference))
        throw new ArgumentException("Model reference must not be null or empty.", nameof(reference));

      if (!TryParseCore(reference, out var model, out var error))
        throw new FormatException($"Invalid model reference '{reference}': {error}");

      return model;
    }

    /// <summary>
    /// Attempts to parse a model reference string without throwing.
    /// </summary>
    /// <param name="reference">The reference string.</param>
    /// <param name="model">The parsed reference, or <c>null</c> when parsing fails.</param>
    /// <returns><c>true</c> when parsing succeeded; otherwise <c>false</c>.</returns>
    public static bool TryParse(string reference, out ModelReference model)
    {
      if (string.IsNullOrWhiteSpace(reference))
      {
        model = null;
        return false;
      }

      return TryParseCore(reference, out model, out _);
    }

    /// <summary>
    /// Normalizes Docker Hub's explicit <c>docker.io/</c> prefix to the same
    /// default-registry form used by Docker Model Runner list output.
    /// </summary>
    /// <param name="reference">The raw model reference.</param>
    /// <returns>The reference without a leading <c>docker.io/</c> prefix.</returns>
    public static string NormalizeDefaultRegistryAlias(string reference)
    {
      return reference != null &&
          reference.StartsWith(DockerHubRegistryPrefix, StringComparison.OrdinalIgnoreCase)
          ? reference[DockerHubRegistryPrefix.Length..]
          : reference;
    }

    private static bool TryParseCore(string reference, out ModelReference model, out string error)
    {
      model = null;
      error = null;

      foreach (var ch in reference)
      {
        if (char.IsWhiteSpace(ch))
        {
          error = "contains whitespace";
          return false;
        }
      }

      reference = NormalizeDefaultRegistryAlias(reference);
      var remainder = reference;
      string digest = null;

      var at = reference.IndexOf('@');
      if (at >= 0)
      {
        if (reference.IndexOf('@', at + 1) >= 0)
        {
          error = "multiple '@' separators";
          return false;
        }

        digest = reference.Substring(at + 1);
        if (!IsValidDigest(digest))
        {
          error = "invalid digest (expected lowercase algorithm:hex with at least 32 hex chars; sha256=64, sha512=128)";
          return false;
        }

        remainder = reference.Substring(0, at);
      }

      var segments = remainder.Split('/');
      foreach (var seg in segments)
      {
        if (seg.Length == 0)
        {
          error = "empty path segment";
          return false;
        }
      }

      string registry = null;
      var start = 0;
      if (segments.Length >= 2 && LooksLikeRegistry(segments[0]))
      {
        registry = segments[0];
        start = 1;
      }

      var lastIndex = segments.Length - 1;
      var nameTag = segments[lastIndex];

      string name;
      string tag = null;
      var colon = nameTag.IndexOf(':');
      if (colon >= 0)
      {
        name = nameTag.Substring(0, colon);
        tag = nameTag.Substring(colon + 1);
        if (name.Length == 0)
        {
          error = "empty name";
          return false;
        }

        if (tag.Length == 0)
        {
          error = "empty tag";
          return false;
        }

        if (tag.Contains(':'))
        {
          error = "invalid tag";
          return false;
        }

        if (!IsValidTag(tag))
        {
          error = "invalid tag (use letters, digits, '.', '_' or '-', starting with a letter, digit or '_')";
          return false;
        }
      }
      else
      {
        name = nameTag;
      }

      for (var i = start; i < lastIndex; i++)
      {
        if (!IsValidNameComponent(segments[i], out var reason))
        {
          error = $"invalid name component '{segments[i]}' ({reason})";
          return false;
        }
      }

      if (!IsValidNameComponent(name, out var nameReason))
      {
        error = $"invalid name component '{name}' ({nameReason})";
        return false;
      }

      string ns = null;
      if (lastIndex - start == 1)
      {
        ns = segments[start];
      }
      else if (lastIndex - start > 1)
      {
        ns = string.Join("/", segments, start, lastIndex - start);
      }

      if (tag == null && digest == null)
        tag = "latest";

      model = new ModelReference(registry, ns, name, tag, digest);
      return true;
    }

    private static bool LooksLikeRegistry(string segment)
    {
      return segment.Contains('.')
          || segment.Contains(':')
          || string.Equals(segment, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidNameComponent(string component, out string reason)
    {
      reason = null;
      if (string.IsNullOrEmpty(component))
      {
        reason = "must not be empty";
        return false;
      }

      if (!IsAsciiLetterOrDigit(component[0]))
      {
        reason = "must start with a letter or digit";
        return false;
      }

      foreach (var c in component)
      {
        if (!IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
        {
          reason = "use only letters, digits, '.', '_' or '-'";
          return false;
        }
      }

      return true;
    }

    private static bool IsValidTag(string tag)
    {
      if (string.IsNullOrEmpty(tag) || tag.Length > 128)
        return false;

      if (!IsAsciiLetterOrDigit(tag[0]) && tag[0] != '_')
        return false;

      foreach (var c in tag)
      {
        if (!IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
          return false;
      }

      return true;
    }

    private static bool IsAsciiLetterOrDigit(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

    /// <summary>Validates the Docker <c>algorithm:hex</c> shape of a content digest.</summary>
    private static bool IsValidDigest(string digest)
    {
      var colon = digest.IndexOf(':');
      if (colon <= 0 || colon == digest.Length - 1)
        return false;

      var algorithm = digest[..colon];
      if (!IsValidDigestAlgorithm(algorithm))
        return false;

      var hexLength = digest.Length - colon - 1;
      if (string.Equals(algorithm, "sha256", StringComparison.Ordinal) && hexLength != 64)
        return false;
      if (string.Equals(algorithm, "sha512", StringComparison.Ordinal) && hexLength != 128)
        return false;
      if (hexLength < 32)
        return false;

      for (var i = colon + 1; i < digest.Length; i++)
      {
        var c = digest[i];
        var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        if (!isHex)
          return false;
      }

      return true;
    }

    private static bool IsValidDigestAlgorithm(string algorithm)
    {
      var previousWasSeparator = false;
      for (var i = 0; i < algorithm.Length; i++)
      {
        var c = algorithm[i];
        var isLowerOrDigit = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        if (isLowerOrDigit)
        {
          previousWasSeparator = false;
          continue;
        }

        if ((c == '.' || c == '+' || c == '_' || c == '-') && i > 0 && !previousWasSeparator)
        {
          previousWasSeparator = true;
          continue;
        }

        return false;
      }

      return !previousWasSeparator;
    }

    /// <summary>
    /// Renders the canonical reference form, e.g. <c>ai/qwen3:latest</c>,
    /// <c>hf.co/org/repo:Q4_K_M</c> or <c>ai/qwen3@sha256:…</c>. Memoized.
    /// </summary>
    /// <returns>The canonical string form.</returns>
    public override string ToString()
    {
      if (_string != null)
        return _string;

      var sb = new StringBuilder();
      if (Registry != null)
        sb.Append(Registry).Append('/');
      if (Namespace != null)
        sb.Append(Namespace).Append('/');
      sb.Append(Name);
      if (Tag != null)
        sb.Append(':').Append(Tag);
      if (Digest != null)
        sb.Append('@').Append(Digest);

      _string = sb.ToString();
      return _string;
    }

    /// <inheritdoc />
    public bool Equals(ModelReference other)
    {
      if (other is null)
        return false;
      if (ReferenceEquals(this, other))
        return true;

      return string.Equals(Registry, other.Registry, StringComparison.OrdinalIgnoreCase)
          && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
          && string.Equals(Name, other.Name, StringComparison.Ordinal)
          && string.Equals(Tag, other.Tag, StringComparison.Ordinal)
          && string.Equals(Digest, other.Digest, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as ModelReference);

    /// <inheritdoc />
    public override int GetHashCode()
    {
      var hash = new HashCode();
      hash.Add(Registry, StringComparer.OrdinalIgnoreCase);
      hash.Add(Namespace, StringComparer.Ordinal);
      hash.Add(Name, StringComparer.Ordinal);
      hash.Add(Tag, StringComparer.Ordinal);
      hash.Add(Digest, StringComparer.Ordinal);
      return hash.ToHashCode();
    }

    /// <summary>Value equality operator.</summary>
    public static bool operator ==(ModelReference left, ModelReference right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(ModelReference left, ModelReference right) => !(left == right);
  }

  /// <summary>
  /// Serializes a <see cref="ModelReference"/> as its canonical string form and
  /// parses it back, so references appear as scalars in DMR request/response JSON.
  /// </summary>
  internal sealed class ModelReferenceJsonConverter : JsonConverter<ModelReference>
  {
    public override ModelReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Null)
        return null;

      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return null;

      if (!ModelReference.TryParse(value, out var model))
        throw new JsonException($"Invalid model reference '{value}'.");

      return model;
    }

    public override void Write(Utf8JsonWriter writer, ModelReference value, JsonSerializerOptions options)
    {
      if (value is null)
        writer.WriteNullValue();
      else
        writer.WriteStringValue(value.ToString());
    }
  }
}
