using System;
using System.Globalization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Shared parsing utilities for CLI output values (percentages, byte sizes, I/O pairs).
  /// Used by both Docker and Podman CLI drivers to avoid duplication.
  /// </summary>
  public static class CliOutputParser
  {
    private static readonly string[] SlashSeparator = [" / "];

    /// <summary>Parses a percentage string (e.g. "5.23%") into a double. Returns 0 on failure.</summary>
    public static double ParsePercent(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;
      var clean = value.TrimEnd('%').Trim();
      return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
          ? result : 0;
    }

    /// <summary>Parses a memory usage string (e.g. "100MiB / 2GiB") into (usage, limit) in bytes.</summary>
    public static (long usage, long limit) ParseMemoryUsage(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return (0, 0);
      var parts = value.Split(SlashSeparator, 2, StringSplitOptions.None);
      return (
        parts.Length > 0 ? ParseByteValue(parts[0].Trim()) : 0,
        parts.Length > 1 ? ParseByteValue(parts[1].Trim()) : 0);
    }

    /// <summary>Parses an I/O pair string (e.g. "1.5kB / 2.3kB") into (first, second) in bytes.</summary>
    public static (long first, long second) ParseIOPair(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return (0, 0);
      var parts = value.Split(SlashSeparator, 2, StringSplitOptions.None);
      return (
        parts.Length > 0 ? ParseByteValue(parts[0].Trim()) : 0,
        parts.Length > 1 ? ParseByteValue(parts[1].Trim()) : 0);
    }

    /// <summary>
    /// Parses a byte value string with suffix.
    /// Uses base-1000 for kB/MB/GB/TB (SI) and base-1024 for KiB/MiB/GiB/TiB (binary).
    /// Longer suffixes are checked first to prevent partial matches.
    /// </summary>
    public static long ParseByteValue(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;
      var s = value.Trim();

      // Order matters: check longer suffixes first to avoid partial matches.
      ReadOnlySpan<(string suffix, double multiplier)> suffixes =
      [
        ("TiB", 1024.0 * 1024 * 1024 * 1024), ("GiB", 1024.0 * 1024 * 1024),
        ("MiB", 1024.0 * 1024), ("KiB", 1024.0),
        ("TB", 1000.0 * 1000 * 1000 * 1000), ("GB", 1000.0 * 1000 * 1000),
        ("MB", 1000.0 * 1000), ("kB", 1000.0), ("KB", 1000.0), ("B", 1.0)
      ];

      foreach (var (suffix, multiplier) in suffixes)
      {
        if (!s.EndsWith(suffix, StringComparison.Ordinal))
          continue;
        var numStr = s.Substring(0, s.Length - suffix.Length).Trim();
        if (double.TryParse(numStr, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var num))
          return (long)(num * multiplier);
        return 0;
      }

      // No suffix — try parsing as raw bytes
      if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
        return (long)raw;
      return 0;
    }
  }
}
