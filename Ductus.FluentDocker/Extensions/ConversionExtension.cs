using System.Linq;
using System.Text.RegularExpressions;

namespace Ductus.FluentDocker.Extensions
{
  public static class ConversionExtension
  {
    /// <summary>
    ///   Converts a numeric expression combined with an optional suffix to denote
    ///   b, k, m, g.
    /// </summary>
    /// <param name="value">The value to be parsed.</param>
    /// <param name="unit">An optional custom array of suffix. But has to be among b, k, m, g.</param>
    /// <returns>If successful the number, otherwise <see cref="long.MinValue" /> is returned.</returns>
    public static long Convert(this string value, params string[] unit)
    {
      if (null == unit || 0 == unit.Length)
        unit = new[] { "b", "k", "m", "g" };

      if (string.IsNullOrWhiteSpace(value))
        return long.MinValue;

      var regex = new Regex(@"(\d+)([a-zA-Z]+)");
      var result = regex.Match(value);

      if (!result.Success)
        return long.MinValue;

      var digits = result.Groups[1].Value;
      var letters = result.Groups[2].Value;

      if (!unit.Contains(letters))
        return long.MinValue;

      if (!long.TryParse(digits, out var val))
        return long.MinValue;

      if (letters == "b")
        return val;

      switch (letters)
      {
        case "b":
          return val;
        case "k":
          return val * 1024;
        case "m":
          return val * 1024 * 1024;
        case "g":
          return val * 1024 * 1024 * 1024;
        default:
          return long.MinValue;
      }
    }
  }
}
