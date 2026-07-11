#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentDocker.Common;

namespace FluentDocker.Model.Common
{
  /// <summary>
  /// Renders FluentDocker path templates such as ${TMP}, ${TEMP}, ${PWD}, ${RND}, and ${E_NAME}.
  /// Template tokens are always expanded when recognized; there is no escape syntax for a literal ${TMP}.
  /// Unset ${E_NAME} environment tokens pass through unchanged.
  /// </summary>
  public sealed partial class TemplateString : IEquatable<TemplateString>
  {
    private static readonly Dictionary<string, Func<string>> Templates;

    static TemplateString() => Templates =
        new Dictionary<string, Func<string>>
        {
          {
            "${TMP}", () =>
            {
              var path = DirectoryHelper.GetTempPath();
              if (path.StartsWith("/var/", StringComparison.Ordinal) && FdOs.IsOsx()) path = "/private/" + path;

              return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
          },
          {
            "${TEMP}", () =>
            {
              var path = DirectoryHelper.GetTempPath();
              if (path.StartsWith("/var/", StringComparison.Ordinal) && FdOs.IsOsx()) path = "/private/" + path;

              return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
          },
          {"${RND}", Path.GetRandomFileName},
          {"${PWD}", Directory.GetCurrentDirectory}
        };

    public TemplateString(string str)
    {
      ArgumentNullException.ThrowIfNull(str);
      Original = str;
      Rendered = Render(str);
    }

    /// <summary>Original template string supplied by the caller.</summary>
    public string Original { get; }

    /// <summary>Rendered string after built-in and environment templates are expanded.</summary>
    public string Rendered { get; }

    private static string Render(string str)
    {
      str = Templates.Keys.Where(key => str.Contains(key, StringComparison.Ordinal))
        .Aggregate(str, (current, key) => ReplaceEach(current, key, Templates[key]));

      return RenderEnvironment(str);
    }

    private static string ReplaceEach(string str, string key, Func<string> valueFactory)
    {
      var index = str.IndexOf(key, StringComparison.Ordinal);
      while (index >= 0)
      {
        var value = valueFactory();
        str = str[..index] + value + str[(index + key.Length)..];
        index = str.IndexOf(key, index + value.Length, StringComparison.Ordinal);
      }

      return str;
    }

    private static string RenderEnvironment(string str)
    {
      if (!str.Contains("${E_", StringComparison.Ordinal))
        return str;

      return EnvironmentTokenRegex().Replace(str, match =>
      {
        var value = Environment.GetEnvironmentVariable(match.Groups["name"].Value);
        return value ?? match.Value;
      });
    }

    public static implicit operator TemplateString?(string? str) => null == str ? null : new TemplateString(str);

    public static implicit operator string?(TemplateString? str) => str?.Rendered;

    public override string ToString()
    {
      return Rendered;
    }

    public bool Equals(TemplateString? other) =>
        other is not null && string.Equals(Rendered, other.Rendered, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as TemplateString);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Rendered);

    public static bool operator ==(TemplateString? left, TemplateString? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(TemplateString? left, TemplateString? right) => !(left == right);

    [GeneratedRegex(@"\$\{E_(?<name>[^}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentTokenRegex();
  }
}
