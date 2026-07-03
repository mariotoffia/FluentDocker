using System;
using System.Buffers;
using System.Text;

namespace FluentDocker.Common
{
  /// <summary>
  /// Shared command-line argument quoting for Docker and Podman CLI drivers.
  /// </summary>
  public static class CommandLineQuoting
  {
    private static readonly SearchValues<char> ShellMetaCharacters =
        SearchValues.Create([' ', '\t', ';', '&', '|', '>', '<', '"', '\'', '$', '`', '!', '*', '?']);

    /// <summary>
    /// Quotes a command-line argument if it contains shell metacharacters, whitespace,
    /// or control characters. Uses the CommandLineToArgvW-compatible backslash rules:
    /// interior backslashes are preserved, backslashes before a literal quote are doubled
    /// plus one escape, and trailing backslashes before the closing quote are doubled.
    /// </summary>
    /// <param name="argument">The argument to quote.</param>
    /// <returns>The original argument, or a safely quoted argument when quoting is required.</returns>
    public static string QuoteArgumentIfNeeded(string argument)
    {
      if (string.IsNullOrEmpty(argument))
        return "\"\"";

      var span = argument.AsSpan();
      var needsQuoting = span.IndexOfAny(ShellMetaCharacters) >= 0;
      if (!needsQuoting)
      {
        foreach (var c in span)
        {
          if (char.IsWhiteSpace(c) || char.IsControl(c))
          {
            needsQuoting = true;
            break;
          }
        }
      }

      if (!needsQuoting)
        return argument;

      var sb = new StringBuilder();
      sb.Append('"');
      for (var i = 0; i < argument.Length; i++)
      {
        var backslashes = 0;
        while (i < argument.Length && argument[i] == '\\')
        {
          backslashes++;
          i++;
        }

        if (i == argument.Length)
        {
          sb.Append('\\', backslashes * 2);
          break;
        }

        if (argument[i] == '"')
        {
          sb.Append('\\', backslashes * 2 + 1);
          sb.Append('"');
        }
        else
        {
          sb.Append('\\', backslashes);
          sb.Append(argument[i]);
        }
      }

      sb.Append('"');
      return sb.ToString();
    }
  }
}
