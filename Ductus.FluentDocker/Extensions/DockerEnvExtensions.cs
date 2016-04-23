using System;
using System.IO;
using System.Linq;
using System.Net;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class DockerEnvExtensions
  {
    private static string _nativeDockerPathCache;
    private static IPAddress _cachedDockerIpAdress;

    public static string ResolveBinary(this string dockerCommand, bool preferMachine = false, bool forceResolve = false)
    {
      string bin = null;
      if (!preferMachine)
      {
        bin = GetBoot2DockerNativeBinPath(forceResolve);
      }

      if (string.IsNullOrEmpty(bin))
      {
        bin = ((TemplateString) "${E_DOCKER_TOOLBOX_INSTALL_PATH}").Rendered;
      }

      dockerCommand = dockerCommand.ToLower();
      switch (dockerCommand)
      {
        case "docker-machine":
          return ((TemplateString) $"{bin}/docker-machine").Rendered.ToPlatformPath();
        case "docker":
          return ((TemplateString) $"{bin}/docker").Rendered.ToPlatformPath();
        case "docker-compose":
          return ((TemplateString) $"{bin}/docker-compose").Rendered.ToPlatformPath();
      }

      throw new ArgumentException($"No command with name {dockerCommand} is present");
    }

    /// <summary>
    ///   Gets the native docker (if any) from the path where it not matches the docker toolbox path (if any).
    /// </summary>
    /// <param name="useCached">If cached is set and this parameter is true, it will use the cached otherwise it will search.</param>
    /// <returns>A path if found to the bin directory where docker binaries resides otherwise null.</returns>
    /// <remarks>
    ///   If docker is installed on multiple locations it will pick the first one in the path and thus conforms
    ///   to path lookup order.
    /// </remarks>
    public static string GetBoot2DockerNativeBinPath(bool useCached = true)
    {
      if (useCached && !string.IsNullOrEmpty(_nativeDockerPathCache))
      {
        return _nativeDockerPathCache;
      }

      var paths = Environment.GetEnvironmentVariable("PATH")?.Split(OsExtensions.IsWindows() ? ';' : ':');
      if (null == paths)
      {
        return null;
      }

      var dockerPaths = paths.Where(x => x.ToLower().Contains("docker")).ToArray();
      if (0 == dockerPaths.Length)
      {
        return null;
      }

      var hasMachine = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH")?.ToLower();
      foreach (var path in dockerPaths)
      {
        if (Directory.GetFiles(path).Any(x => Path.GetFileName(x)?.ToLower().StartsWith("docker") ?? false))
        {
          if (path.ToLower() == hasMachine)
          {
            continue;
          }

          _nativeDockerPathCache = path;
          return path;
        }
      }

      return null;
    }

    public static bool IsMachine()
    {
      return null != Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH");
    }

    public static bool IsEmulatedNative()
    {
      return !OsExtensions.IsUnix() && null != GetBoot2DockerNativeBinPath();
    }

    public static bool IsNative()
    {
      return OsExtensions.IsUnix();
    }

    public static IPAddress EmulatedNativeAdress(bool useCache = true)
    {
      if (useCache && null != _cachedDockerIpAdress)
      {
        return _cachedDockerIpAdress;
      }

      var hostEntry = Dns.GetHostEntry("docker");
      if (hostEntry.AddressList.Length > 0)
      {
        _cachedDockerIpAdress = hostEntry.AddressList[0];
      }

      return _cachedDockerIpAdress;
    }
  }
}