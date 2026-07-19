#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        // A value counts as pre-wrapped only when the outer quotes are balanced around the WHOLE
        // value (no unescaped interior '"' closing the wrap early, closing quote not escaped).
        // Unwrapping also unescapes \" and \\ so re-escaping below is semantically
        // lossless under the documented grammar (a lone backslash re-escapes to \\,
        // which parses back to the same value).
        var unwrapped = IsBalancedWrap(rawValue)
            ? Unescape(rawValue[1..^1])
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
      if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        return false;

      var escaped = false;
      for (var i = 1; i < value.Length - 1; i++)
      {
        if (escaped)
        {
          escaped = false;
          continue;
        }
        if (value[i] == '\\')
        {
          escaped = true;
          continue;
        }
        // An unescaped interior quote means the leading quote closes early — the outer quotes do
        // not wrap the whole value (e.g. "x" y="z"), so the value must be treated as literal.
        if (value[i] == '"')
          return false;
      }

      // A trailing active escape would make the closing quote part of the value, not the wrap.
      return !escaped;
    }

    /// <summary>Inverse of the wrap-escaping: <c>\\</c> → <c>\</c> and <c>\"</c> → <c>"</c>; other sequences are kept verbatim.</summary>
    private static string Unescape(string value)
    {
      if (!value.Contains('\\'))
        return value;

      var sb = new StringBuilder(value.Length);
      for (var i = 0; i < value.Length; i++)
      {
        if (value[i] == '\\' && i + 1 < value.Length && (value[i + 1] == '\\' || value[i + 1] == '"'))
        {
          sb.Append(value[i + 1]);
          i++;
          continue;
        }
        sb.Append(value[i]);
      }

      return sb.ToString();
    }
  }
}
