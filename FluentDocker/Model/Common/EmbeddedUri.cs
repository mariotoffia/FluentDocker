#nullable enable
using System;

namespace FluentDocker.Model.Common
{
  /// <summary>
  /// Uri identifying a resource embedded in a .NET assembly, on the format
  /// <c>emb:AssemblyName/namespace/resource</c>.
  /// </summary>
  public sealed class EmbeddedUri : Uri
  {
    internal const string Prefix = "emb";

    private readonly string _assembly;

    /// <summary>
    ///   Uri to use when managing embedded resources.
    /// </summary>
    /// <param name="embedded">Uri on format emb:AssemblyName/namespace/resource</param>
    public EmbeddedUri(string embedded) : this(Parse(embedded))
    {
    }

    private EmbeddedUri(Parts parts) : base(parts.Original)
    {
      _assembly = parts.Assembly;
      Namespace = parts.Namespace;
      Resource = parts.Resource;
    }

    /// <summary>The assembly name segment of the embedded URI.</summary>
    public string Assembly => _assembly;
    /// <summary>The namespace segment identifying where the resource is embedded.</summary>
    public string Namespace { get; }
    /// <summary>The resource name segment; <c>null</c> when the URI only specifies assembly and namespace.</summary>
    public string? Resource { get; }

    /// <summary>Returns whether the value can be parsed as an embedded-resource URI.</summary>
    public static bool IsValid(string? embedded)
    {
      return TryParseParts(embedded, out _);
    }

    /// <summary>Parses a string as an <see cref="EmbeddedUri"/>; <c>null</c> input yields <c>null</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is not a valid <c>emb:</c> URI.</exception>
    public static implicit operator EmbeddedUri?(string? uri)
    {
      if (null == uri)
      {
        return null;
      }

      return new EmbeddedUri(uri);
    }

    private static Parts Parse(string embedded)
    {
      if (TryParseParts(embedded, out var parts))
        return parts;

      if (string.IsNullOrWhiteSpace(embedded) ||
          embedded.StartsWith(Prefix + ":", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Expected format emb:AssemblyName/namespace/resource.", nameof(embedded));

      throw new ArgumentException($"Incorrect scheme (expecting: {Prefix}) for embedded uri - {embedded}",
        nameof(embedded));
    }

    private static bool TryParseParts(string? embedded, out Parts parts)
    {
      parts = default;
      if (string.IsNullOrWhiteSpace(embedded))
        return false;

      var split = embedded.Split(':', 2);
      if (split.Length != 2 ||
          !string.Equals(split[0], Prefix, StringComparison.OrdinalIgnoreCase))
        return false;

      var segments = split[1].Split('/', 3);
      if (segments.Length < 2 ||
          string.IsNullOrWhiteSpace(segments[0]) ||
          string.IsNullOrWhiteSpace(segments[1]))
        return false;

      parts = new Parts(embedded, segments[0], segments[1], segments.Length > 2 ? segments[2] : null);
      return true;
    }

    private readonly record struct Parts(string Original, string Assembly, string Namespace, string? Resource);
  }
}
