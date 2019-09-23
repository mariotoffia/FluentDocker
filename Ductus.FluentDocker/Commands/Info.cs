using System;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Commands
{
  public static class Info
  {
    /// <summary>
    /// Get the docker version both client and server version. It includes the server operating system (linux, windows).
    /// </summary>
    /// <param name="host">The docker daemon to contact.</param>
    /// <param name="certificates">Path to certificates if any.</param>
    /// <returns>A response with the versions if successfull.</returns>
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
      return version.Data.ServerOs.ToLower().Equals("windows");
    }

    public static bool IsLinuxEngine(this DockerUri host, ICertificatePaths certificates = null)
    {
      return !IsWindowsEngine(host, certificates);
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