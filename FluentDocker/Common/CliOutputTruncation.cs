#nullable enable
using System.Globalization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Constants and helpers for marking truncated CLI output.
  /// </summary>
  public static class CliOutputTruncation
  {
    /// <summary>
    /// Default number of trailing characters to keep from long CLI output.
    /// </summary>
    public const int DefaultTailChars = 256 * 1024;

    /// <summary>
    /// Creates the marker prepended to truncated CLI output.
    /// </summary>
    /// <param name="maxChars">The number of trailing characters retained.</param>
    /// <returns>The truncation marker text.</returns>
    public static string Marker(int maxChars) =>
        string.Create(CultureInfo.InvariantCulture, $"[FluentDocker: output truncated, showing last {maxChars} chars]");
  }
}
