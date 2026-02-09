using System.Runtime.InteropServices;

namespace FluentDocker.Common
{
  public static class FdOs
  {
    public static bool IsWindows()
      => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsOsx()
      => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsLinux()
      => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
  }
}
