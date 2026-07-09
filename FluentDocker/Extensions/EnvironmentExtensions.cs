#nullable enable
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// Helpers for rendering Dockerfile ENV and LABEL name/value pairs.
  /// </summary>
  public static class EnvironmentExtensions
  {
    /// <summary>
    /// Extracts the name/value text and verifies that it is valid. It then
    /// make sure that the value is wrapped inside double quotes if not yet wrapped.
    /// </summary>
    /// <param name="nameValue">The name=value strings</param>
    /// <returns>A list of name=value string with the value wrapped inside double quotes.</returns>
    public static IList<string> WrapValue(this TemplateString[] nameValue)
    {

      var list = new List<string>();
      foreach (var s in nameValue.Select(s => s.Rendered))
      {

        var index = s.IndexOf('=');
        if (-1 == index)
        {
          throw new FluentDockerException(
              $"Expected format name=value, missing equal sign in the name value string: '{s}'"
            );
        }

        var name = s[..index];
        if (string.IsNullOrWhiteSpace(name))
          throw new FluentDockerException(
              $"Expected format name=value, empty name in the name value string: '{s}'"
            );
        ValidateName(name);
        var rawValue = s[(index + 1)..];
        var unwrapped = rawValue.Length >= 2 && rawValue.StartsWith('"') && IsBalancedWrap(rawValue)
            ? rawValue[1..^1]
            : rawValue;
        ValidateValue(unwrapped);
        var value = $"\"{unwrapped.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        list.Add($"{name}={value}");
      }

      return list;
    }

    private static void ValidateName(string name)
    {
      foreach (var c in name)
      {
        if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '"' || c == '\'')
          throw new FluentDockerException(
              "Dockerfile ENV/LABEL names cannot contain whitespace, quotes, control characters, or newlines.");
      }
    }

    private static void ValidateValue(string value)
    {
      foreach (var c in value)
      {
        if ((char.IsControl(c) && c != '\t') || c == '\u2028' || c == '\u2029')
          throw new FluentDockerException(
              "Dockerfile ENV/LABEL values cannot contain control or Unicode line separator characters except tab.");
      }
    }

    private static bool IsBalancedWrap(string value)
    {
      if (!value.EndsWith('"'))
        return false;
      var backslashes = 0;
      for (var i = value.Length - 2; i >= 0 && value[i] == '\\'; i--)
        backslashes++;
      // ponytail: even backslashes means the final quote closes the wrap, not an escaped value quote.
      return backslashes % 2 == 0;
    }
  }
}
