using System;

namespace Ductus.FluentDocker.Extensions
{
  public static class OsExtensions
  {
    public static bool IsWindows()
    {
      return Environment.OSVersion.Platform != PlatformID.MacOSX &&
             Environment.OSVersion.Platform != PlatformID.Unix;
    }

    public static bool IsUnix()
    {
      return Environment.OSVersion.Platform == PlatformID.Unix;
    }

    public static bool IsMac()
    {
      return Environment.OSVersion.Platform == PlatformID.MacOSX;
    }

    public static string ToPlatformPath(this string path)
    {
      if (!IsWindows())
      {
        return path;
      }

      if (path.Length > 2 && path[1] == ':' && path[2] == '\\')
      {
        return path.Replace('/', '\\');
      }

      return string.IsNullOrEmpty(path) ? path : $"{path[2]}:{path.Substring(3).Replace('/', '\\')}";
    }

    public static string ToMsysPath(this string path)
    {
      if (!IsWindows())
      {
        return path;
      }

      return "//" + char.ToLower(path[0]) + path.Substring(2).Replace('\\', '/');
    }
  }
}