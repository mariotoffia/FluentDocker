using System;

namespace Ductus.FluentDocker.Extensions
{
  internal static class OsExtensions
  {
    internal static bool IsWindows()
    {
      return Environment.OSVersion.Platform != PlatformID.MacOSX && 
             Environment.OSVersion.Platform != PlatformID.Unix;
    }

    internal static string ToPlatformPath(this string path)
    {
      if (!OsExtensions.IsWindows())
      {
        return path;
      }

      return string.IsNullOrEmpty(path) ? path : $"{path[2]}:{path.Substring(3).Replace('/', '\\')}";
    }

    internal static string ToMsysPath(this string path)
    {
      if (!OsExtensions.IsWindows())
      {
        return path;
      }

      return "//" + char.ToLower(path[0]) + path.Substring(2).Replace('\\', '/');
    }
  }
}