#nullable enable
using System;
using System.Buffers;
using System.Text;

namespace FluentDocker.Common
{
  /// <summary>
  /// Quotes individual arguments for direct process execution.
  /// </summary>
  /// <remarks>
  /// These helpers implement Windows <c>CommandLineToArgvW</c>/<c>CreateProcess</c>
  /// argv quoting semantics for callers that pass arguments directly to a child
  /// process. They are not shell escaping helpers. POSIX shell metacharacters
  /// such as <c>$</c>, backtick, and <c>!</c> are not escaped because no
  /// <c>/bin/sh -c</c> shell interprets them. Never concatenate these quoted
  /// arguments into a command string routed through a shell.
  /// </remarks>
  public static class CommandLineQuoting
  {
    private static readonly SearchValues<char> ShellMetaCharacters =
        SearchValues.Create([' ', '\t', ';', '&', '|', '>', '<', '"', '\'', '$', '`', '!', '*', '?']);

    /// <summary>
    /// Quotes one argv argument when it contains characters that require quoting for
    /// direct child-process execution.
    /// </summary>
    /// <remarks>
    /// Uses <c>CommandLineToArgvW</c>-compatible backslash rules: interior
    /// backslashes are preserved, backslashes before a literal quote are doubled
    /// plus one escape, and trailing backslashes before the closing quote are doubled.
    /// This is not POSIX shell escaping; callers must not pass the returned value
    /// through <c>/bin/sh -c</c> or another shell.
    /// </remarks>
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
