using System;

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
        while (i < duration.Length && char.IsDigit(duration[i]))
          i++;
        if (start == i || !long.TryParse(duration[start..i], out var value))
          return null;

        long multiplier;
        if (duration[i..].StartsWith("ms", StringComparison.Ordinal))
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

        total += value * multiplier;
      }

      return total;
    }
  }
}
