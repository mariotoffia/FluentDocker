using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Extensions
{
  public static class CommandExtensions
  {
    private static string _nativeDockerPathCache;
    private static IPAddress _cachedDockerIpAdress;

    /// <summary>
    ///   Reads a <see cref="ConsoleStream{T}" /> until <see cref="ConsoleStream{T}.IsFinished" /> is set to true
    ///   or a timeout occured on a read.
    /// </summary>
    /// <typeparam name="T">The type of returned items in the console stream.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="millisTimeout">
    ///   The amount of time to wait on a single <see cref="ConsoleStream{T}.TryRead" /> before returning.
    /// </param>
    /// <returns>A list of items read from the console stream.</returns>
    public static IList<T> ReadToEnd<T>(this ConsoleStream<T> stream, int millisTimeout = 5000) where T : class
    {
      var list = new List<T>();
      while (!stream.IsFinished)
      {
        var line = stream.TryRead(millisTimeout);
        if (null == line)
        {
          break;
        }
        list.Add(line);
      }

      return list;
    }

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

      var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Environment.OSVersion.IsWindows() ? ';' : ':');
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
      return !Environment.OSVersion.IsUnix() && null != GetBoot2DockerNativeBinPath();
    }

    public static bool IsNative()
    {
      return Environment.OSVersion.IsUnix();
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
        // Prefer IPv4 addresses
        IPAddress v4Addr = hostEntry.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        _cachedDockerIpAdress = v4Addr ?? hostEntry.AddressList[0];
      }

      return _cachedDockerIpAdress;
    }

    internal static string RenderBaseArgs(this Uri host, ICertificatePaths certificates = null)
    {
      var args = string.Empty;
      if (null != host)
      {
        args = $" -H {host.Host}:{host.Port}";
      }

      if (null == certificates)
      {
        return args;
      }

      args +=
        $" --tlsverify=true --tlscacert={certificates.CaCertificate} --tlscert={certificates.ClientCertificate} --tlskey={certificates.ClientKey}";

      return args;
    }
  }
}