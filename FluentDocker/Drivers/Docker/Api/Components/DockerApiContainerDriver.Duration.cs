using System;
using System.Globalization;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiContainerDriver
  {
    private static long? ParseDurationNanoseconds(string duration)
    {
      if (string.IsNullOrEmpty(duration))
        return null;

      if (long.TryParse(duration, out var raw))
        return raw;

      long total = 0;
      var i = 0;
      while (i < duration.Length)
      {
        var start = i;
        while (i < duration.Length && (char.IsDigit(duration[i]) || duration[i] == '.'))
          i++;
        if (start == i || !decimal.TryParse(duration[start..i], NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value))
          return null;

        long multiplier;
        if (duration[i..].StartsWith("ns", StringComparison.Ordinal))
        {
          multiplier = 1;
          i += 2;
        }
        else if (duration[i..].StartsWith("us", StringComparison.Ordinal) ||
                 duration[i..].StartsWith("µs", StringComparison.Ordinal))
        {
          multiplier = 1_000;
          i += 2;
        }
        else if (duration[i..].StartsWith("ms", StringComparison.Ordinal))
        {
          multiplier = 1_000_000;
          i += 2;
        }
        else if (i < duration.Length && duration[i] == 's')
        {
          multiplier = 1_000_000_000;
          i++;
        }
        else if (i < duration.Length && duration[i] == 'm')
        {
          multiplier = 60 * 1_000_000_000L;
          i++;
        }
        else if (i < duration.Length && duration[i] == 'h')
        {
          multiplier = 60 * 60 * 1_000_000_000L;
          i++;
        }
        else
        {
          return null;
        }

        var nanos = value * multiplier;
        if (nanos > long.MaxValue || nanos > long.MaxValue - total)
          return null;
        total += (long)nanos;
      }

      return total;
    }
  }
}
