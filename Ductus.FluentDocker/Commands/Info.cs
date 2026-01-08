using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Common;
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
      return version.Data.ServerOs.ToLower().Equals("windows");
    }

    public static bool IsLinuxEngine(this DockerUri host, ICertificatePaths certificates = null)
    {
      return !IsWindowsEngine(host, certificates);
    }

    public static CommandResponse<string> Switch(this DockerUri host, ICertificatePaths certificates = null)
    {
      // dockercli is Docker Desktop for Windows specific - not available with Podman
      if (CommandExtensions.ActiveContainerEngine == ContainerEngine.Podman)
      {
        return new CommandResponse<string>(true, new List<string> { "Podman does not support daemon switching" }, "", "Podman does not support daemon switching");
      }

      try
      {
        var args = $"{host.RenderBaseArgs(certificates)}";
        return new ProcessExecutor<NoLineResponseParser, string>(
          "dockercli".ResolveBinary(), $"{args} -SwitchDaemon").Execute();
      }
      catch (FluentDockerException ex) when (ex.Message.Contains("dockercli"))
      {
        return new CommandResponse<string>(false, new List<string>(), "dockercli binary not found - this is only available with Docker Desktop for Windows", default(string));
      }
    }

    public static CommandResponse<string> LinuxDaemon(this DockerUri host, ICertificatePaths certificates = null)
    {
      // dockercli is Docker Desktop for Windows specific - not available with Podman
      if (CommandExtensions.ActiveContainerEngine == ContainerEngine.Podman)
      {
        return new CommandResponse<string>(true, new List<string> { "Podman always uses Linux containers" }, "", "Podman always uses Linux containers");
      }

      try
      {
        var args = $"{host.RenderBaseArgs(certificates)}";
        return new ProcessExecutor<NoLineResponseParser, string>(
          "dockercli".ResolveBinary(), $"{args} -SwitchLinuxEngine").Execute();
      }
      catch (FluentDockerException ex) when (ex.Message.Contains("dockercli"))
      {
        return new CommandResponse<string>(false, new List<string>(), "dockercli binary not found - this is only available with Docker Desktop for Windows", default(string));
      }
    }
    
    public static CommandResponse<string> WindowsDaemon(this DockerUri host, ICertificatePaths certificates = null)
    {
      // dockercli is Docker Desktop for Windows specific - not available with Podman
      if (CommandExtensions.ActiveContainerEngine == ContainerEngine.Podman)
      {
        return new CommandResponse<string>(false, new List<string>(), "Podman does not support Windows containers", default(string));
      }

      try
      {
        var args = $"{host.RenderBaseArgs(certificates)}";
        return new ProcessExecutor<NoLineResponseParser, string>(
          "dockercli".ResolveBinary(), $"{args} -SwitchWindowsEngine").Execute();
      }
      catch (FluentDockerException ex) when (ex.Message.Contains("dockercli"))
      {
        return new CommandResponse<string>(false, new List<string>(), "dockercli binary not found - this is only available with Docker Desktop for Windows", default(string));
      }
    }
  }
}
