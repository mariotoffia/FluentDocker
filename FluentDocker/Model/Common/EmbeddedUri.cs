#nullable enable
using System;

namespace FluentDocker.Model.Common
{
  public sealed class EmbeddedUri : Uri
  {
    internal const string Prefix = "emb";

    private readonly string _assembly;

    /// <summary>
    ///   Uri to use when managing embedded resources.
    /// </summary>
    /// <param name="embedded">Uri on format emb:AssemblyName/namespace/resource</param>
    public EmbeddedUri(string embedded) : base(Validate(embedded))
    {
      var split = embedded.Split(':', 2);
      var s = split[1].Split('/', 3);

      _assembly = s[0];
      Namespace = s[1];

      if (s.Length > 2)
      {
        Resource = s[2];
      }
    }

    public string Assembly => _assembly;
    public string Namespace { get; }
    public string? Resource { get; }

    public static implicit operator EmbeddedUri?(string? uri)
    {
      if (null == uri)
      {
        return null;
      }

      return new EmbeddedUri(uri);
    }

    private static string Validate(string embedded)
    {
      if (string.IsNullOrWhiteSpace(embedded))
        throw new ArgumentException("Expected format emb:AssemblyName/namespace/resource.", nameof(embedded));

      var split = embedded.Split(':', 2);
      if (split.Length != 2 ||
          !string.Equals(split[0], Prefix, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException($"Incorrect scheme (expecting: {Prefix}) for embedded uri - {embedded}",
          nameof(embedded));

      var segments = split[1].Split('/', 3);
      if (segments.Length < 2 ||
          string.IsNullOrWhiteSpace(segments[0]) ||
          string.IsNullOrWhiteSpace(segments[1]))
        throw new ArgumentException("Expected format emb:AssemblyName/namespace/resource.", nameof(embedded));

      return embedded;
    }
  }
}
