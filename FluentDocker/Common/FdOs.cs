using System.Runtime.InteropServices;

namespace FluentDocker.Common
{
  /// <summary>
  /// OS detection helpers used by FluentDocker to select platform-specific behavior.
  /// </summary>
  public static class FdOs
  {
    /// <summary>Returns true if the current platform is Windows.</summary>
    public static bool IsWindows()
      => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Returns true if the current platform is macOS.</summary>
    public static bool IsOsx()
      => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>Returns true if the current platform is Linux.</summary>
    public static bool IsLinux()
      => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
  }
}
