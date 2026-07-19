#nullable enable

using System;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// String helpers used by FluentDocker's legacy extension surface.
  /// </summary>
  public static class StringExtensions
  {
    /// <summary>
    /// Wraps a string with the supplied boundary string unless that boundary already exists.
    /// </summary>
    /// <param name="s">The string to wrap.</param>
    /// <param name="c">The string to check and wrap with if not existing.</param>
    /// <returns>The wrapped string.</returns>
    public static string? WrapWithChar(this string? s, string c)
    {
      if (s == null || string.IsNullOrEmpty(c))
        return s;

      if (s.Length == 0)
        return c + c;

      if (s == c)
        return c + s + c;

      if (!s.StartsWith(c, StringComparison.Ordinal))
      {
        s = c + s;
      }

      if (!s.EndsWith(c, StringComparison.Ordinal))
      {
        s += c;
      }

      return s;
    }
  }
}
