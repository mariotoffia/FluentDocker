using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// A focused implementation of Docker's <c>.dockerignore</c> matching rules, used to
  /// exclude files from a build-context tar. Supports the common Docker semantics:
  /// blank lines and <c>#</c> comments are ignored; patterns are anchored to the context
  /// root; <c>*</c> matches within a single path segment, <c>?</c> matches a single
  /// character, and <c>**</c> matches across multiple segments; a trailing <c>/</c> or a
  /// directory pattern excludes the whole subtree; <c>!pattern</c> re-includes (negation)
  /// and later rules override earlier ones (last match wins).
  ///
  /// The Dockerfile and the <c>.dockerignore</c> file itself are never excluded, matching
  /// Docker which always sends them.
  /// </summary>
  public sealed class DockerIgnoreFilter
  {
    private readonly IReadOnlyList<Rule> _rules;
    private readonly string _dockerfileName;

    private sealed record Rule(Regex Pattern, bool Negated);

    private DockerIgnoreFilter(IReadOnlyList<Rule> rules, string dockerfileName)
    {
      _rules = rules;
      _dockerfileName = dockerfileName;
    }

    /// <summary>
    /// Loads <c>.dockerignore</c> from <paramref name="contextPath"/> if present.
    /// Returns an empty (match-nothing) filter when the file is absent.
    /// </summary>
    public static DockerIgnoreFilter Load(string contextPath, string dockerfileName = "Dockerfile")
    {
      var ignorePath = Path.Combine(contextPath, ".dockerignore");
      var lines = File.Exists(ignorePath)
          ? File.ReadAllLines(ignorePath)
          : Array.Empty<string>();
      return FromLines(lines, dockerfileName);
    }

    /// <summary>
    /// Builds a filter from raw <c>.dockerignore</c> lines. Exposed for unit testing.
    /// </summary>
    public static DockerIgnoreFilter FromLines(
        IEnumerable<string> lines, string dockerfileName = "Dockerfile")
    {
      ArgumentNullException.ThrowIfNull(lines);

      var rules = new List<Rule>();
      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line[0] == '#')
          continue;

        var negated = false;
        if (line[0] == '!')
        {
          negated = true;
          line = line[1..].Trim();
        }

        if (OperatingSystem.IsWindows())
          line = line.Replace('\\', '/');
        if (line.StartsWith("./", StringComparison.Ordinal))
          line = line[2..];
        // Leading '/' anchors to the context root; our relative paths are already
        // root-relative, so we just strip it. A trailing '/' marks a directory pattern;
        // subtree exclusion is handled uniformly by the regex suffix below.
        if (line.Length == 0)
          continue;
        var directoryOnly = line[^1] == '/';
        line = line.Trim('/');
        if (line.Length == 0)
          continue;

        rules.Add(new Rule(BuildRegex(line, directoryOnly), negated));
      }

      return new DockerIgnoreFilter(rules, dockerfileName);
    }

    /// <summary>
    /// Returns <c>true</c> when the context-relative path should be excluded from the tar.
    /// </summary>
    public bool IsIgnored(string relativePath)
    {
      ArgumentNullException.ThrowIfNull(relativePath);

      var path = OperatingSystem.IsWindows()
          ? relativePath.Replace('\\', '/').TrimStart('/')
          : relativePath.TrimStart('/');

      // Docker always includes the Dockerfile and .dockerignore regardless of rules.
      var dockerfileComparison = OperatingSystem.IsWindows()
          ? StringComparison.OrdinalIgnoreCase
          : StringComparison.Ordinal;
      if (string.Equals(path, _dockerfileName, dockerfileComparison) ||
          string.Equals(path, ".dockerignore", StringComparison.Ordinal))
        return false;

      var ignored = false;
      foreach (var rule in _rules)
      {
        if (rule.Pattern.IsMatch(path))
          ignored = !rule.Negated;
      }
      return ignored;
    }

    /// <summary>
    /// Translates a single <c>.dockerignore</c> glob into an anchored regex. The trailing
    /// <c>(?:/.*)?</c> makes any match also cover the subtree beneath it (directory
    /// semantics), which is harmless for plain file patterns.
    /// </summary>
    private static Regex BuildRegex(string pattern, bool directoryOnly)
    {
      var sb = new StringBuilder("^");
      var i = 0;
      while (i < pattern.Length)
      {
        var c = pattern[i];
        if (c == '*')
        {
          if (i + 1 < pattern.Length && pattern[i + 1] == '*')
          {
            // '**' — match across path segments.
            i += 2;
            if (i < pattern.Length && pattern[i] == '/')
            {
              i++;
              sb.Append("(?:.*/)?"); // zero or more leading segments
            }
            else
            {
              sb.Append(".*");
            }
          }
          else
          {
            sb.Append("[^/]*"); // single segment
            i++;
          }
        }
        else if (c == '?')
        {
          sb.Append("[^/]");
          i++;
        }
        else if (c == '[')
        {
          var close = FindClassEnd(pattern, i);
          if (close < 0)
          {
            // Unterminated '[' — treat as a literal character.
            sb.Append(Regex.Escape("["));
            i++;
          }
          else
          {
            sb.Append('[');
            var j = i + 1;
            if (pattern[j] == '!') // Docker uses [!...] for negation; regex uses [^...].
            {
              sb.Append('^');
              j++;
            }
            if (j < close && pattern[j] == ']') // a leading ']' is a literal class member
            {
              sb.Append("\\]");
              j++;
            }
            for (; j < close; j++)
            {
              var cc = pattern[j];
              if (cc == '\\' || cc == ']' || cc == '^')
                sb.Append('\\').Append(cc); // escape regex-special class chars
              else
                sb.Append(cc); // preserve ranges like 0-9 verbatim
            }
            sb.Append(']');
            i = close + 1;
          }
        }
        else if (c == '/')
        {
          sb.Append('/');
          i++;
        }
        else if (c == '\\' && !OperatingSystem.IsWindows() && i + 1 < pattern.Length)
        {
          sb.Append(Regex.Escape(pattern[i + 1].ToString()));
          i += 2;
        }
        else
        {
          sb.Append(Regex.Escape(c.ToString()));
          i++;
        }
      }

      sb.Append(directoryOnly ? "/.*$" : "(?:/.*)?$");
      return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Finds the index of the <c>]</c> that closes the character class beginning at
    /// <paramref name="start"/> (which points at the <c>[</c>), or <c>-1</c> if unterminated.
    /// A <c>]</c> immediately after <c>[</c> or <c>[!</c> is a literal member, not the close.
    /// </summary>
    private static int FindClassEnd(string pattern, int start)
    {
      var j = start + 1;
      if (j < pattern.Length && pattern[j] == '!')
        j++;
      if (j < pattern.Length && pattern[j] == ']')
        j++;
      for (; j < pattern.Length; j++)
        if (pattern[j] == ']')
          return j;
      return -1;
    }
  }
}
