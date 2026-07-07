#nullable enable
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;

namespace FluentDocker.Extensions
{
  public static class EnvironmentExtensions
  {
    /// <summary>
    /// This function will extract the name value and verify that is is valid. It will then
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
        var rawValue = s[(index + 1)..];
        var unwrapped = rawValue.Length >= 2 && rawValue.StartsWith('"') && IsBalancedWrap(rawValue)
            ? rawValue[1..^1]
            : rawValue;
        var value = $"\"{unwrapped.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        list.Add($"{name}={value}");
      }

      return list;
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
