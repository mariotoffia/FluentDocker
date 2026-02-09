using System;
using System.Collections.Generic;

namespace FluentDocker.Common
{
  /// <summary>
  /// Parses a shell command string into individual arguments, respecting
  /// single and double quoted strings. Quotes are stripped from the output
  /// but spaces inside quotes are preserved.
  /// </summary>
  public static class ShellArgParser
  {
    /// <summary>
    /// Splits a command string into individual arguments using shell-like
    /// quoting rules. Single-quoted and double-quoted substrings are kept
    /// as single arguments with the surrounding quotes removed. Content
    /// inside single quotes is taken literally (double quotes are
    /// preserved). Content inside double quotes is taken literally
    /// (single quotes are preserved).
    /// </summary>
    /// <param name="command">The command string to parse.</param>
    /// <returns>
    /// An array of argument strings. Returns an empty array when
    /// <paramref name="command"/> is null, empty, or whitespace-only.
    /// </returns>
    public static string[] Parse(string command)
    {
      if (string.IsNullOrWhiteSpace(command))
        return Array.Empty<string>();

      var args = new List<string>();
      var current = new List<char>();
      var inSingleQuote = false;
      var inDoubleQuote = false;

      for (var i = 0; i < command.Length; i++)
      {
        var c = command[i];

        if (inSingleQuote)
        {
          if (c == '\'')
          {
            inSingleQuote = false;
          }
          else
          {
            current.Add(c);
          }
        }
        else if (inDoubleQuote)
        {
          if (c == '"')
          {
            inDoubleQuote = false;
          }
          else if (c == '\\' && i + 1 < command.Length)
          {
            var next = command[i + 1];
            if (next == '"' || next == '\\')
            {
              current.Add(next);
              i++; // skip the escaped character
            }
            else
            {
              current.Add(c); // literal backslash
            }
          }
          else
          {
            current.Add(c);
          }
        }
        else if (c == '\'')
        {
          inSingleQuote = true;
        }
        else if (c == '"')
        {
          inDoubleQuote = true;
        }
        else if (c == '\\' && i + 1 < command.Length)
        {
          current.Add(command[i + 1]);
          i++; // skip escaped character
        }
        else if (char.IsWhiteSpace(c))
        {
          if (current.Count > 0)
          {
            args.Add(new string(current.ToArray()));
            current.Clear();
          }
        }
        else
        {
          current.Add(c);
        }
      }

      if (current.Count > 0)
      {
        args.Add(new string(current.ToArray()));
      }

      return args.ToArray();
    }
  }
}
