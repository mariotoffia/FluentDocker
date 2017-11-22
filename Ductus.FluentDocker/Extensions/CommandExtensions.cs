using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Model.Containers;
using OperatingSystem = Ductus.FluentDocker.Common.OperatingSystem;

namespace Ductus.FluentDocker.Extensions
{
  public static class CommandExtensions
  {
    private static DockerBinariesResolver _binaryResolver = new DockerBinariesResolver();

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
          break;
        list.Add(line);
      }

      return list;
    }

    public static string ResolveBinary(this string dockerCommand, bool preferMachine = false, bool forceResolve = false)
    {
      if (forceResolve)
        _binaryResolver = new DockerBinariesResolver();

      return _binaryResolver.Resolve(dockerCommand, preferMachine).FqPath;
    }

    public static IEnumerable<string> GetResolvedBinaries()
    {
      if (null == _binaryResolver)
        _binaryResolver = new DockerBinariesResolver();

      return new List<string>
      {
        "docker        : " + (_binaryResolver.MainDockerClient.FqPath ?? "not found"),
        "docker-compose: " + (_binaryResolver.MainDockerCompose.FqPath ?? "not found"),
        "docker-machine: " + (_binaryResolver.MainDockerMachine.FqPath ?? "not found")
      };
    }

    /// <summary>
    ///   Checks is the current main environment is toolbox or not.
    /// </summary>
    /// <returns>Returns true if toolbox, false otherwise.</returns>
    public static bool IsToolbox()
    {
      return _binaryResolver.MainDockerClient.IsToolbox;
    }

    public static bool IsEmulatedNative()
    {
      return !OperatingSystem.IsLinux() && !IsToolbox();
    }


    public static bool IsDockerDnsAvailable()
    {
      try
      {
        Dns.GetHostEntryAsync("docker").Wait();
        return true;
      }
      catch (AggregateException ex)
        when (ex.InnerExceptions.Count == 1 && ex.InnerExceptions[0] is SocketException)
      {
        return false;
      }
    }

    public static bool IsNative()
    {
      return OperatingSystem.IsLinux();
    }

    public static IPAddress EmulatedNativeAdress(bool useCache = true)
    {
      if (useCache && null != _cachedDockerIpAdress)
        return _cachedDockerIpAdress;

      var hostEntry = Dns.GetHostEntryAsync("docker").Result;
      if (hostEntry.AddressList.Length > 0)
      {
        // Prefer IPv4 addresses
        var v4Addr = hostEntry.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        _cachedDockerIpAdress = v4Addr ?? hostEntry.AddressList[0];
      }

      return _cachedDockerIpAdress;
    }

    internal static string RenderBaseArgs(this Uri host, ICertificatePaths certificates = null)
    {
      var args = string.Empty;
      if (null != host)
      {
        args = host.Port == -1 ? $" -H {host}" : $" -H {host.Host}:{host.Port}";
      }

      if (null == certificates)
        return args;

      args +=
        $" --tlsverify=true --tlscacert={certificates.CaCertificate} --tlscert={certificates.ClientCertificate} --tlskey={certificates.ClientKey}";

      return args;
    }
  }
}