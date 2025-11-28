using System;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker system/info commands.
  /// </summary>
  /// <remarks>
  /// This class is deprecated. Use the ISystemDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [System.Obsolete("Use ISystemDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class Info
  {
    /// <summary>
    /// Get the docker version both client and server version. It includes the server operating system (linux, windows).
    /// </summary>
    /// <param name="host">The docker daemon to contact.</param>
    /// <param name="certificates">Path to certificates if any.</param>
    /// <returns>A response with the versions if successful.</returns>
    public static CommandResponse<DockerInfoBase> Version(this DockerUri host, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var fmt = "{{.Server.Version}};{{.Server.APIVersion}};{{.Client.Version}};{{.Client.APIVersion}};{{.Server.Os}}";

      return
        new ProcessExecutor<BaseInfoResponseParser, DockerInfoBase>(
          "docker".ResolveBinary(),
          $"{args} version -f \"{fmt}\"").Execute();
    }

    public static bool IsWindowsEngine(this DockerUri host, ICertificatePaths certificates = null)
    {
      var version = host.Version(certificates);
      if (!version.Success || version.Data == null || string.IsNullOrEmpty(version.Data.ServerOs))
        return false;
      return version.Data.ServerOs.Equals("windows", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLinuxEngine(this DockerUri host, ICertificatePaths certificates = null)
    {
      var version = host.Version(certificates);
      if (!version.Success || version.Data == null || string.IsNullOrEmpty(version.Data.ServerOs))
        return true; // Default to Linux if we can't determine
      return !version.Data.ServerOs.Equals("windows", StringComparison.OrdinalIgnoreCase);
    }

    public static CommandResponse<string> Switch(this DockerUri host, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      return new ProcessExecutor<NoLineResponseParser, string>(
        "dockercli".ResolveBinary(), $"{args} -SwitchDaemon").Execute();
    }

    public static CommandResponse<string> LinuxDaemon(this DockerUri host, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      return new ProcessExecutor<NoLineResponseParser, string>(
        "dockercli".ResolveBinary(), $"{args} -SwitchLinuxEngine").Execute();
    }
    public static CommandResponse<string> WindowsDaemon(this DockerUri host, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      return new ProcessExecutor<NoLineResponseParser, string>(
        "dockercli".ResolveBinary(), $"{args} -SwitchWindowsEngine").Execute();
    }
  }
}
