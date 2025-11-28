using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Commands;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Images;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker client commands (container operations).
  /// </summary>
  /// <remarks>
  /// This class is deprecated. Use the IContainerDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [Obsolete("Use IContainerDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class Client
  {
    #region New struct-based command methods

    /// <summary>
    /// Logs in to a Docker registry using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> LoginCommand(this DockerUri host, LoginCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} login {options} {args.Server}").Execute();
    }

    /// <summary>
    /// Lists containers using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> PsCommand(this DockerUri host, PsCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      // Ensure quiet and no-trunc are included for backward compatibility
      if (!options.Contains("--quiet"))
        options += " --quiet";
      if (!options.Contains("--no-trunc"))
        options += " --no-trunc";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} ps {options}").Execute();
    }

    /// <summary>
    /// Stops a container using command args struct.
    /// </summary>
    public static CommandResponse<string> StopCommand(this DockerUri host, StopCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".ResolveBinary(),
        $"{certArgs} stop {options} {args.ContainerId}").Execute();
    }

    /// <summary>
    /// Removes a container using command args struct.
    /// </summary>
    public static CommandResponse<string> RemoveContainerCommand(this DockerUri host, RemoveContainerCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return new ProcessExecutor<SingleStringResponseParser, string>(
        "docker".ResolveBinary(),
        $"{certArgs} rm {options} {args.ContainerId}").Execute();
    }

    /// <summary>
    /// Executes a command in a container using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ExecuteCommand(this DockerUri host, ExecCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var command = args.Command ?? "";
      var cmdArgs = args.Arguments != null ? " " + string.Join(" ", args.Arguments) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} exec {options} {args.ContainerId} {command}{cmdArgs}").Execute();
    }

    /// <summary>
    /// Exports a container using command args struct.
    /// </summary>
    public static CommandResponse<string> ExportCommand(this DockerUri host, ExportCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";

      return new ProcessExecutor<NoLineResponseParser, string>("docker".ResolveBinary(),
        $"{certArgs} export -o {args.OutputFilePath} {args.ContainerId}").Execute();
    }

    /// <summary>
    /// Copies files to a container using command args struct.
    /// </summary>
    public static CommandResponse<string> CopyToContainerCommand(this DockerUri host, CopyCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return new ProcessExecutor<ProcessExitAwareResponseParser, string>("docker".ResolveBinary(),
        $"{certArgs} cp {options} \"{args.HostPath}\" {args.ContainerId}:{args.ContainerPath}").Execute();
    }

    /// <summary>
    /// Copies files from a container using command args struct.
    /// </summary>
    public static CommandResponse<string> CopyFromContainerCommand(this DockerUri host, CopyCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return new ProcessExecutor<ProcessExitAwareResponseParser, string>("docker".ResolveBinary(),
        $"{certArgs} cp {options} {args.ContainerId}:{args.ContainerPath} \"{args.HostPath}\"").Execute();
    }

    /// <summary>
    /// Pulls an image using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> PullCommand(this DockerUri host, PullCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} pull {options} {args.Image}").Execute();
    }

    /// <summary>
    /// Builds an image using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> BuildCommand(this DockerUri host, BuildCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var context = string.IsNullOrEmpty(args.Context) ? "." : args.Context;

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} build {options} {context}").Execute();
    }

    /// <summary>
    /// Creates a container using command args struct.
    /// </summary>
    public static CommandResponse<string> CreateCommand(this DockerUri host, CreateCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var command = !string.IsNullOrEmpty(args.Command) ? $" {args.Command}" : "";
      var cmdArgs = args.Arguments != null && args.Arguments.Length > 0 ? " " + string.Join(" ", args.Arguments) : "";

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{certArgs} create {options} {args.Image}{command}{cmdArgs}").Execute();
    }

    /// <summary>
    /// Runs a container using command args struct.
    /// </summary>
    public static CommandResponse<string> RunCommand(this DockerUri host, RunCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var command = !string.IsNullOrEmpty(args.Command) ? $" {args.Command}" : "";
      var cmdArgs = args.Arguments != null && args.Arguments.Length > 0 ? " " + string.Join(" ", args.Arguments) : "";

      return
        new ProcessExecutor<SingleStringResponseParser, string>(
          "docker".ResolveBinary(),
          $"{certArgs} run {options} {args.Image}{command}{cmdArgs}").Execute();
    }

    /// <summary>
    /// Starts containers using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> StartCommand(this DockerUri host, StartCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var ids = args.ContainerIds != null ? string.Join(" ", args.ContainerIds) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} start {options} {ids}").Execute();
    }

    /// <summary>
    /// Inspects containers/images using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> InspectCommand(this DockerUri host, InspectCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var ids = args.Ids != null ? string.Join(" ", args.Ids) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} inspect {options} {ids}").Execute();
    }

    /// <summary>
    /// Pauses containers using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> PauseCommand(this DockerUri host, PauseCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var ids = args.ContainerIds != null ? string.Join(" ", args.ContainerIds) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} pause {ids}").Execute();
    }

    /// <summary>
    /// Unpauses containers using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> UnpauseCommand(this DockerUri host, UnpauseCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var ids = args.ContainerIds != null ? string.Join(" ", args.ContainerIds) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} unpause {ids}").Execute();
    }

    /// <summary>
    /// Kills containers using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> KillCommand(this DockerUri host, KillCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var ids = args.ContainerIds != null ? string.Join(" ", args.ContainerIds) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} kill {options} {ids}").Execute();
    }

    /// <summary>
    /// Restarts containers using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> RestartCommand(this DockerUri host, RestartCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var ids = args.ContainerIds != null ? string.Join(" ", args.ContainerIds) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} restart {options} {ids}").Execute();
    }

    #endregion

    #region Existing methods (backward compatible)
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

    #endregion
  }
}
