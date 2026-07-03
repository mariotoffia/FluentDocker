namespace FluentDocker.Common
{
  internal static class CliOutputTruncation
  {
    public const int DefaultTailBytes = 256 * 1024;

    public static string Marker(int maxBytes) =>
        $"[FluentDocker: output truncated, showing last {maxBytes} bytes]";
  }
}
