using FluentDocker.Common;

namespace FluentDocker.Extensions
{
  public static class OsExtensions
  {
    public static string ToMsysPath(this string path)
    {
      if (!FdOs.IsWindows() || string.IsNullOrEmpty(path) || path.Length < 3)
        return path;

      return "//" + char.ToLower(path[0]) + path[2..].Replace('\\', '/');
    }
  }
}
