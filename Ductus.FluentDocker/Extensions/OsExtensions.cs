using System;

namespace Ductus.FluentDocker.Extensions
{
  public static class OsExtensions
  {
    public static bool IsWindows(this OperatingSystem os)
    {
      return os.Platform != PlatformID.MacOSX &&
             os.Platform != PlatformID.Unix;
    }

 
    public static bool IsUnix(this OperatingSystem os)
    {
      return os.Platform == PlatformID.Unix;
    }

    public static bool IsMac(this OperatingSystem os)
    {
      return os.Platform == PlatformID.MacOSX;
    }

    public static string ToPlatformPath(this string path)
    {
      if (!Environment.OSVersion.IsWindows())
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
      if (!Environment.OSVersion.IsWindows())
      {
        return path;
      }

      return "//" + char.ToLower(path[0]) + path.Substring(2).Replace('\\', '/');
    }
  }
}