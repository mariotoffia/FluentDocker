using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Commands
{
  public static class Client
  {
    public static CommandResponse<IList<string>> Ps(this Uri host, string options = null, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      if (string.IsNullOrEmpty(options))
      {
        options = "--quiet";
      }

      if (-1 == options.IndexOf("--quiet", StringComparison.Ordinal))
      {
        options += " --quiet";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".DockerPath(),
          $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} ps {options}").Execute();
    }

    public static CommandResponse<string> Create(this Uri host, string image, string command = null,
      string[] args = null, ContainerCreateParams prms = null, CertificatePaths certificates = null)
    {
      var certArgs = RenderBaseArgs(host, certificates?.CaCertificate, certificates?.ClientCertificate,
        certificates?.ClientKey);

      var arg = $"{certArgs} create";
      if (null != prms)
      {
        arg += " " + prms;
      }

      arg += " " + image;

      if (!string.IsNullOrEmpty(command))
      {
        arg += $" {command}";
      }

      if (null != args && 0 != args.Length)
      {
        arg += " " + string.Join(" ", args);
      }

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".DockerPath(),
          arg).Execute();
    }

    public static CommandResponse<string> Run(this Uri host, string image, ContainerCreateParams args = null,
      CertificatePaths certificates = null)
    {
      return Run(host, image, args, certificates?.CaCertificate, certificates?.ClientCertificate,
        certificates?.ClientKey);
    }

    public static CommandResponse<string> Run(this Uri host, string image, ContainerCreateParams args = null,
      string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      var arg = $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} run -d";
      if (null != args)
      {
        arg += " " + args;
      }

      arg += " " + image;

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".DockerPath(),
          arg).Execute();
    }

    public static CommandResponse<string> Stop(this Uri host, string id, TimeSpan? killTimeout = null,
      CertificatePaths certificates = null)
    {
      return Stop(host, id, killTimeout, certificates?.CaCertificate,
        certificates?.ClientCertificate, certificates?.ClientKey);
    }

    public static CommandResponse<IList<string>> Start(this Uri host, string id, CertificatePaths certificates = null)
    {
      var certArgs = RenderBaseArgs(host, certificates?.CaCertificate, certificates?.ClientCertificate,
        certificates?.ClientKey);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".DockerPath(),
          $"{certArgs} start {id}").Execute();
    }

    public static CommandResponse<string> Stop(this Uri host, string id, TimeSpan? killTimeout = null,
      string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      var arg = $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} stop";
      if (null != killTimeout)
      {
        arg += $" --time={Math.Round(killTimeout.Value.TotalSeconds, 0)}";
      }

      arg += $" {id}";

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".DockerPath(),
        arg).Execute();
    }

    public static CommandResponse<string> RemoveContainer(this Uri host, string id, bool force = false,
      bool removeVolumes = false,
      string removeLink = null, CertificatePaths certificates = null)
    {
      return RemoveContainer(host, id, force, removeVolumes, removeLink, certificates?.CaCertificate,
        certificates?.ClientCertificate, certificates?.ClientKey);
    }

    public static CommandResponse<string> RemoveContainer(this Uri host, string id, bool force = false,
      bool removeVolumes = false,
      string removeLink = null, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      var arg = $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} rm";
      if (force)
      {
        arg += " --force";
      }

      if (removeVolumes)
      {
        arg += " --volumes";
      }

      if (!string.IsNullOrEmpty(removeLink))
      {
        arg += $" --link {removeLink}";
      }

      arg += $" {id}";

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".DockerPath(),
        arg).Execute();
    }

    public static CommandResponse<Processes> Top(this Uri host, string id, CertificatePaths certificates = null)
    {
      var arg =
        $"{RenderBaseArgs(host, certificates?.CaCertificate, certificates?.ClientCertificate, certificates?.ClientKey)} top {id}";
      return new ProcessExecutor<ClientTopResponseParser, Processes>("docker".DockerPath(),
        arg).Execute();
    }

    public static CommandResponse<Container> InspectContainer(this Uri host, string id,
      CertificatePaths certificates = null)
    {
      return InspectContainer(host, id, certificates?.CaCertificate, certificates?.ClientCertificate,
        certificates?.ClientKey);
    }

    public static CommandResponse<Container> InspectContainer(this Uri host, string id, string caCertPath = null,
      string clientCertPath = null,
      string clientKeyPath = null)
    {
      return new ProcessExecutor<ClientContainerInspectCommandResponder, Container>("docker".DockerPath(),
        $"{RenderBaseArgs(host, caCertPath, clientCertPath, clientKeyPath)} inspect {id}").Execute();
    }

    public static CommandResponse<string> Export(this Uri host, string id, string fqFilePath,
      CertificatePaths certificates)
    {
      var arg =
        $"{RenderBaseArgs(host, certificates?.CaCertificate, certificates?.ClientCertificate, certificates?.ClientKey)} export";
      return new ProcessExecutor<NoLineResponseParser, string>("docker".DockerPath(),
        $"{arg} -o {fqFilePath} {id}").Execute();
    }

    private static string RenderBaseArgs(Uri host, string caCertPath = null, string clientCertPath = null,
      string clientKeyPath = null)
    {
      return null != caCertPath
        ? $" -H {host.Host}:{host.Port} --tlsverify=true --tlscacert={caCertPath} --tlscert={clientCertPath} --tlskey={clientKeyPath}"
        : string.Empty;
    }
  }
}