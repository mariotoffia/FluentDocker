using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Commands
{
  public static class Client
  {
    public static CommandResponse<IList<string>> Login(this DockerUri host, string server, string user = null,
      string pass = null, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = string.Empty;
      if (!string.IsNullOrEmpty(user))
        opts = $"-u {user}";
      if (!string.IsNullOrEmpty(pass))
        opts += $" -p {pass}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} login {opts} {server}").Execute();
    }

    public static CommandResponse<IList<string>> Logout(this DockerUri host, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} logout").Execute();
    }

    public static CommandResponse<IList<string>> Pull(this DockerUri host, string image, ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} pull {image}").Execute();
    }

    public static CommandResponse<IList<string>> Pause(this DockerUri host, ICertificatePaths certificates = null, params string[] containerIds)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} pause {string.Join(" ", containerIds)}").Execute();
    }

    public static CommandResponse<IList<string>> UnPause(this DockerUri host, ICertificatePaths certificates = null, params string[] containerIds)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} unpause {string.Join(" ", containerIds)}").Execute();
    }

    public static CommandResponse<IList<string>> Build(this DockerUri host, string name, string tag, string workdir = null,
      ContainerBuildParams prms = null,
      ICertificatePaths certificates = null)
    {
      if (null == tag)
      {
        tag = "latest";
      }

      if (string.IsNullOrEmpty(workdir))
      {
        workdir = ".";
      }

      var options = string.Empty;
      if (null != prms?.Tags)
      {
        if (!prms.Tags.Any(x => x == tag))
        {
          options = $"-t {name}:{tag}";
        }
      }

      if (null != prms)
      {
        options += $" {prms}";
      }

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{host.RenderBaseArgs(certificates)} build {options} {workdir}").Execute();
    }

    public static CommandResponse<IList<DockerImageRowResponse>> Images(this DockerUri host, ICertificatePaths certificates = null,
      params string[] filters)
    {
      var options = new System.Text.StringBuilder();
      options.Append("--quiet --no-trunc --format \"{{.ID}};{{.Repository}};{{.Tag}}\"");

      foreach (var filter in filters)
      {
        if (!string.IsNullOrEmpty(filter))
        {
          options.Append($" --filter=\"{filter}\"");
        }
      }

      return
        new ProcessExecutor<ClientImagesResponseParser, IList<DockerImageRowResponse>>(
          "docker".ResolveBinary(),
          $"{host.RenderBaseArgs(certificates)} images {options.ToString()}").Execute();
    }

    public static CommandResponse<IList<string>> Ps(this DockerUri host, string options = null,
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
          $"{host.RenderBaseArgs(certificates)} ps {options}").Execute();
    }

    public static CommandResponse<string> Create(this DockerUri host, string image, string command = null,
      string[] args = null, ContainerCreateParams prms = null, ICertificatePaths certificates = null)
    {
      var certArgs = host.RenderBaseArgs(certificates);

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

    public static CommandResponse<string> Run(this DockerUri host, string image, ContainerCreateParams args = null,
      ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)} run -d";
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

    public static CommandResponse<IList<string>> Execute(this DockerUri host, string id, string execArgs, ICertificatePaths certificates = null)
    {
      var certArgs = host.RenderBaseArgs(certificates);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} exec -i {id} {execArgs}").Execute();
    }

    public static CommandResponse<IList<string>> Start(this DockerUri host, string id, ICertificatePaths certificates = null)
    {
      var certArgs = host.RenderBaseArgs(certificates);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} start {id}").Execute();
    }

    public static CommandResponse<string> Stop(this DockerUri host, string id, TimeSpan? killTimeout = null,
      ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)} stop";
      if (null != killTimeout)
      {
        arg += $" --time={Math.Round(killTimeout.Value.TotalSeconds, 0)}";
      }

      arg += $" {id}";

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".ResolveBinary(),
        arg).Execute();
    }

    public static CommandResponse<string> RemoveContainer(this DockerUri host, string id, bool force = false,
      bool removeVolumes = false,
      string removeLink = null, ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)} rm";
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

    public static CommandResponse<Processes> Top(this DockerUri host, string id, ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)} top {id}";
      return new ProcessExecutor<ClientTopResponseParser, Processes>("docker".ResolveBinary(),
        arg).Execute();
    }

    public static CommandResponse<Container> InspectContainer(this DockerUri host, string id,
      ICertificatePaths certificates = null)
    {
      return new ProcessExecutor<ClientContainerInspectCommandResponder, Container>("docker".ResolveBinary(),
        $"{host.RenderBaseArgs(certificates)} inspect {id}").Execute();
    }

    public static CommandResponse<IList<Container>> InspectContainers(this DockerUri host,
      ICertificatePaths certificates = null,
      params string[] containerIds)
    {
      if (containerIds?.Any() != true)
      {
        var psResult = host.Ps("--all", certificates);
        if (!psResult.Success)
          return new CommandResponse<IList<Container>>(psResult.Success, psResult.Log, psResult.Error);

        containerIds = psResult.Data.ToArray();
      }

      var dockerBinary = "docker".ResolveBinary();
      return new ProcessExecutor<ClientInspectContainersResponseParser, IList<Container>>(dockerBinary,
          $"{host.RenderBaseArgs(certificates)} inspect " + string.Join(" ", containerIds))
        .Execute();
    }

    public static CommandResponse<ImageConfig> InspectImage(this DockerUri host, string id,
      ICertificatePaths certificates = null)
    {
      return new ProcessExecutor<ClientImageInspectCommandResponder, ImageConfig>("docker".ResolveBinary(),
        $"{host.RenderBaseArgs(certificates)} image inspect {id}").Execute();
    }

    public static CommandResponse<string> Export(this DockerUri host, string id, string fqFilePath,
      ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)} export";
      return new ProcessExecutor<NoLineResponseParser, string>("docker".ResolveBinary(),
        $"{arg} -o {fqFilePath} {id}").Execute();
    }

    public static CommandResponse<string> CopyToContainer(this DockerUri host, string id, string containerPath,
      string hostPath, ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)}";
      return new ProcessExecutor<ProcessExitAwareResponseParser, string>("docker".ResolveBinary(),
        $"{arg} cp \"{hostPath}\" {id}:{containerPath}").Execute();
    }

    public static CommandResponse<string> CopyFromContainer(this DockerUri host, string id, string containerPath,
      string hostPath, ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)}";
      return new ProcessExecutor<ProcessExitAwareResponseParser, string>("docker".ResolveBinary(),
        $"{arg} cp {id}:{containerPath} \"{hostPath}\"").Execute();
    }

    public static CommandResponse<IList<Diff>> Diff(this DockerUri host, string id, ICertificatePaths certificates = null)
    {
      var arg = $"{host.RenderBaseArgs(certificates)}";
      return new ProcessExecutor<ClientDiffResponseParser, IList<Diff>>("docker".ResolveBinary(),
        $"{arg} diff {id}").Execute();
    }
  }
}
