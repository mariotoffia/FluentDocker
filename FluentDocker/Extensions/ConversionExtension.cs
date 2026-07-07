#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FluentDocker.Extensions
{
  public static partial class ConversionExtension
  {
    private static readonly string[] DefaultUnits = ["b", "k", "m", "g"];
    /// <summary>
    ///   Converts a numeric expression combined with an optional suffix to denote
    ///   b, k, m, g.
    /// </summary>
    /// <param name="value">The value to be parsed.</param>
    /// <param name="unit">An optional custom array of suffix. But has to be among b, k, m, g.</param>
    /// <returns>If successful the number, otherwise <see cref="long.MinValue" /> is returned.</returns>
    [Obsolete("Use FluentDocker.Common.CliOutputParser for Docker/Podman size output; this legacy helper has a narrower unit grammar.")]
    public static long Convert(this string value, params string[] unit)
    {
      if (null == unit || 0 == unit.Length)
        unit = DefaultUnits;

      if (string.IsNullOrWhiteSpace(value))
        return long.MinValue;

      var regex = MyRegex();
      var result = regex.Match(value);

      if (!result.Success)
        return long.MinValue;

      var digits = result.Groups[1].Value;
      var letters = result.Groups[2].Value.ToLowerInvariant();
      if (string.IsNullOrEmpty(letters))
        letters = "b";

      if (!unit.Select(x => x.ToLowerInvariant()).Contains(letters))
        return long.MinValue;

      if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var val))
        return long.MinValue;

      decimal bytes;
      try
      {
        bytes = letters switch
        {
          "b" => val,
          "k" => val * 1024,
          "m" => val * 1024 * 1024,
          "g" => val * 1024 * 1024 * 1024,
          _ => long.MinValue,
        };
      }
      catch (OverflowException)
      {
        return long.MinValue;
      }

      if (bytes < 0 || bytes > long.MaxValue)
        return long.MinValue;

      return (long)bytes;
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)([a-zA-Z]*)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MyRegex();
  }
}
