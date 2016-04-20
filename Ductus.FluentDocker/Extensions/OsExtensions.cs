using System;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class OsExtensions
  {
    public static string DockerPath(this string dockerCommand)
    {
      dockerCommand = dockerCommand.ToLower();
      switch (dockerCommand)
      {
        case "docker-machine":
          return HasMachine()
            ? ((TemplateString)"${E_DOCKER_TOOLBOX_INSTALL_PATH}/docker-machine").Rendered.ToPlatformPath()
            : dockerCommand;
        case "docker":
          return HasMachine()
            ? ((TemplateString)"${E_DOCKER_TOOLBOX_INSTALL_PATH}/docker").Rendered.ToPlatformPath()
            : dockerCommand;
      }

      throw new ArgumentException($"No command with name {dockerCommand} is present");
    }

    private static bool HasMachine()
    {
      return null != Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH");
    }

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