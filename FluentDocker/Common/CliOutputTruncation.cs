#nullable enable
namespace FluentDocker.Common
{
  public static class CliOutputTruncation
  {
    public const int DefaultTailChars = 256 * 1024;

    public static string Marker(int maxChars) =>
        $"[FluentDocker: output truncated, showing last {maxChars} chars]";
  }
}
