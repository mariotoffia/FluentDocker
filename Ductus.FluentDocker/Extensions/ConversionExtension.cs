using System.Linq;

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

      var num = value.Substring(0, value.Length - 2);
      var u = value.Substring(value.Length - 2, 1).ToLower();

      if (char.IsDigit(u[0]))
        return !long.TryParse(value, out var n) ? long.MinValue : n;

      if (!unit.Contains(u))
        return long.MinValue;

      if (!long.TryParse(num, out var val))
        return long.MinValue;

      if (u == "b")
        return val;

      switch (u)
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
