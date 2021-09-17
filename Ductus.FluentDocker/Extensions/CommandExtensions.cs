using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Extensions
{
  public static class CommandExtensions
  {
    private static IPAddress _cachedDockerIpAddress;
    private static SudoMechanism _sudoMechanism = SudoMechanism.None;
    private static string _sudoPassword;
    private static string _defaultShell = "bash";

    private static DockerBinariesResolver _binaryResolver = new DockerBinariesResolver(_sudoMechanism, _sudoPassword);

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

    /// <summary>
    /// Changes the default shell when <see cref="SudoMechanism"/> is either NoPassword or Password.
    /// </summary>
    /// <param name="shell">The new default shell to use.</param>
    /// <remarks>
    /// By default FluentDocker uses bash.
    /// </remarks>
    public static void AsDefaultShell(this string shell)
    {
      _defaultShell = shell;
    }

    /// <summary>
    /// Gets the shell to use when <see cref="SudoMechanism"/> is either NoPassword or Password.
    /// </summary>
    public static string DefaultShell => _defaultShell;

    /// <summary>
    /// Sets the sudo mechanism on subsequent commands.
    /// </summary>
    /// <param name="sudo">The wanted sudo mechanism.</param>
    /// <param name="password">Optional. If sudo mechanism is set to SudoMechanism.Password it is required></param>
    /// <exception cref="ArgumentException">If sudo mechanism password is wanted but no password was provided.</exception>
    /// <remarks>
    /// By default the library operates on SudoMechanism.None and therefore expects the current user to be able to
    /// communicate with the docker daemon.
    /// </remarks>
    [Experimental]
    public static void SetSudo(this SudoMechanism sudo, string password = null)
    {
      if (string.IsNullOrWhiteSpace(password) && sudo == SudoMechanism.Password)
        throw new ArgumentException("When using SudoMechanism.Password a password must be provided!", nameof(password));

      _sudoMechanism = sudo;
      _sudoPassword = password;
      _binaryResolver = new DockerBinariesResolver(_sudoMechanism, _sudoPassword);
    }

    public static string ResolveBinary(this string dockerCommand, bool preferMachine = false, bool forceResolve = false)
    {
      if (forceResolve || null == _binaryResolver)
        _binaryResolver = new DockerBinariesResolver(_sudoMechanism, _sudoPassword);

      return dockerCommand.ResolveBinary(_binaryResolver, preferMachine);
    }

    public static string ResolveBinary(this string dockerCommand, DockerBinariesResolver resolver, bool preferMachine = false)
    {
      var binary = resolver.Resolve(dockerCommand, preferMachine);

      if (FdOs.IsWindows() || binary.Sudo == SudoMechanism.None)
        return binary.FqPath;

      string cmd;
      if (binary.Sudo == SudoMechanism.NoPassword)
        cmd = $"sudo {binary.FqPath}";
      else
        cmd = $"echo {binary.SudoPassword} | sudo -S {binary.FqPath}";

      if (string.IsNullOrEmpty(cmd))
      {
        if (!string.IsNullOrEmpty(dockerCommand) && dockerCommand.ToLower() == "docker-machine")
          throw new FluentDockerException(
            $"Could not find {dockerCommand} make sure it is on your path. From 2.2.0 you have to seprately install it via https://github.com/docker/machine/releases");

        throw new FluentDockerException($"Could not find {dockerCommand}, make sure it is on your path.");
      }

      return cmd;
    }

    public static bool IsMachineBinaryPresent()
    {
      if (null == _binaryResolver)
        _binaryResolver = new DockerBinariesResolver(_sudoMechanism, _sudoPassword);

      return null != _binaryResolver.MainDockerMachine;

    }

    public static bool IsComposeBinaryPresent()
    {
      if (null == _binaryResolver)
        _binaryResolver = new DockerBinariesResolver(_sudoMechanism, _sudoPassword);

      return null != _binaryResolver.MainDockerCompose;

    }

    public static IEnumerable<string> GetResolvedBinaries()
    {
      if (null == _binaryResolver)
        _binaryResolver = new DockerBinariesResolver(_sudoMechanism, _sudoPassword);

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
      return !FdOs.IsLinux() && !IsToolbox();
    }


    public static bool IsDockerDnsAvailable()
    {
      try
      {
#if NETSTANDARD1_6
        Dns.GetHostEntryAsync("host.docker.internal").Wait();
#else
        Dns.GetHostEntry("host.docker.internal");
#endif
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
      return FdOs.IsLinux();
    }

    public static IPAddress EmulatedNativeAddress(bool useCache = true)
    {
      if (useCache && null != _cachedDockerIpAddress)
        return _cachedDockerIpAddress;

#if NETSTANDARD1_6
      var hostEntry = Dns.GetHostEntryAsync("host.docker.internal").Result;
#else
      var hostEntry = Dns.GetHostEntry("host.docker.internal");
#endif
      if (hostEntry.AddressList.Length > 0)
      {
        // Prefer IPv4 addresses
        var v4Address = hostEntry.AddressList.LastOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        _cachedDockerIpAddress = v4Address ?? hostEntry.AddressList.Last();
      }

      return _cachedDockerIpAddress;
    }

    internal static string RenderBaseArgs(this Uri host, ICertificatePaths certificates = null)
    {
      var args = string.Empty;
      if (null != host)
      {
        args = $" -H {host}";
      }

      if (null == certificates)
        return args;

      args +=
        $" --tlsverify --tlscacert={certificates.CaCertificate} --tlscert={certificates.ClientCertificate} --tlskey={certificates.ClientKey}";

      return args;
    }
  }
}
