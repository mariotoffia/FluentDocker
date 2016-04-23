using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Commands
{
  public static class Client
  {
    public static CommandResponse<IList<string>> Ps(this Uri host, string options = null,
      ICertificatePaths certificates = null)
    {
      if (string.IsNullOrEmpty(options))
      {
        options = "--quiet --no-trunc";
      }

      if (-1 == options.IndexOf("--quiet", StringComparison.Ordinal))
      {
        options += " --quiet";
      }

      if (-1 == options.IndexOf("--no-trunc", StringComparison.Ordinal))
      {
        options += " --no-trunc";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{RenderBaseArgs(host, certificates)} ps {options}").Execute();
    }

    public static CommandResponse<string> Create(this Uri host, string image, string command = null,
      string[] args = null, ContainerCreateParams prms = null, ICertificatePaths certificates = null)
    {
      var certArgs = RenderBaseArgs(host, certificates);

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
          "docker".ResolveBinary(),
          arg).Execute();
    }

    public static CommandResponse<string> Run(this Uri host, string image, ContainerCreateParams args = null,
      ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)} run -d";
      if (null != args)
      {
        arg += " " + args;
      }

      arg += " " + image;

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          arg).Execute();
    }

    public static CommandResponse<IList<string>> Start(this Uri host, string id, ICertificatePaths certificates = null)
    {
      var certArgs = RenderBaseArgs(host, certificates);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} start {id}").Execute();
    }

    public static CommandResponse<string> Stop(this Uri host, string id, TimeSpan? killTimeout = null,
      ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)} stop";
      if (null != killTimeout)
      {
        arg += $" --time={Math.Round(killTimeout.Value.TotalSeconds, 0)}";
      }

      arg += $" {id}";

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".ResolveBinary(),
        arg).Execute();
    }

    public static CommandResponse<string> RemoveContainer(this Uri host, string id, bool force = false,
      bool removeVolumes = false,
      string removeLink = null, ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)} rm";
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
        "docker".ResolveBinary(),
        arg).Execute();
    }

    public static CommandResponse<Processes> Top(this Uri host, string id, ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)} top {id}";
      return new ProcessExecutor<ClientTopResponseParser, Processes>("docker".ResolveBinary(),
        arg).Execute();
    }

    public static CommandResponse<Container> InspectContainer(this Uri host, string id,
      ICertificatePaths certificates = null)
    {
      return new ProcessExecutor<ClientContainerInspectCommandResponder, Container>("docker".ResolveBinary(),
        $"{RenderBaseArgs(host, certificates)} inspect {id}").Execute();
    }

    public static CommandResponse<string> Export(this Uri host, string id, string fqFilePath,
      ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)} export";
      return new ProcessExecutor<NoLineResponseParser, string>("docker".ResolveBinary(),
        $"{arg} -o {fqFilePath} {id}").Execute();
    }

    public static CommandResponse<string> CopyToContainer(this Uri host, string id, string containerPath,
      string hostPath, ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)}";
      return new ProcessExecutor<IgnoreErrorResponseParser, string>("docker".ResolveBinary(),
        $"{arg} cp \"{hostPath}\" {id}:{containerPath}").Execute();
    }

    public static CommandResponse<string> CopyFromContainer(this Uri host, string id, string containerPath,
      string hostPath, ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)}";
      return new ProcessExecutor<IgnoreErrorResponseParser, string>("docker".ResolveBinary(),
        $"{arg} cp {id}:{containerPath} \"{hostPath}\"").Execute();
    }

    public static CommandResponse<IList<Diff>> Diff(this Uri host, string id, ICertificatePaths certificates = null)
    {
      var arg = $"{RenderBaseArgs(host, certificates)}";
      return new ProcessExecutor<ClientDiffResponseParser, IList<Diff>>("docker".ResolveBinary(),
        $"{arg} diff {id}").Execute();
    }

    private static string RenderBaseArgs(Uri host, ICertificatePaths certificates = null)
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