using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class OsExtensions
  {
    public static string ToMsysPath(this string path)
    {
      if (!FdOs.IsWindows())
        return path;

      return "//" + char.ToLower(path[0]) + path.Substring(2).Replace('\\', '/');
    }
  }
}