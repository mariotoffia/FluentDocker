using System;
using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Commands;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Stacks;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Commands to manage docker stack
  /// </summary>
  /// <remarks>
  /// See good examples at https://github.com/play-with-docker/stacks
  /// This class is deprecated. Use the IStackDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [System.Obsolete("Use IStackDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class Stack
  {
    #region New struct-based command methods

    /// <summary>
    /// Lists stacks using command args struct.
    /// </summary>
    public static CommandResponse<IList<StackLsResponse>> StackLsCommand(this DockerUri host, StackLsCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      if (!options.Contains("--format"))
        options += " --format=\"{{.Name}};{{.Services}};{{.Orchestrator}};{{.Namespace}}\"";

      return
        new ProcessExecutor<StackLsResponseParser, IList<StackLsResponse>>(
          "docker".ResolveBinary(),
          $"{certArgs} stack ls {options}").Execute();
    }

    /// <summary>
    /// Lists tasks in a stack using command args struct.
    /// </summary>
    public static CommandResponse<IList<StackPsResponse>> StackPsCommand(this DockerUri host, StackPsCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      if (!options.Contains("--format"))
        options += " --format=\"{{.ID}};{{.Name}};{{.Image}};{{.Node}};{{.DesiredState}};{{.CurrentState}};{{.Error}};{{.Ports}}\"";

      return
        new ProcessExecutor<StackPsResponseParser, IList<StackPsResponse>>(
          "docker".ResolveBinary(),
          $"{certArgs} stack ps {options} {args.Stack}").Execute();
    }

    /// <summary>
    /// Removes stacks using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> StackRmCommand(this DockerUri host, StackRmCommandArgs args)
    {
      if (args.Stacks == null || args.Stacks.Count == 0)
        throw new ArgumentException("Must provide stacks when doing rm.", nameof(args.Stacks));

      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var stacks = string.Join(" ", args.Stacks);

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} stack rm {options} {stacks}").Execute();
    }

    /// <summary>
    /// Deploys a stack using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> StackDeployCommand(this DockerUri host, StackDeployCommandArgs args)
    {
      if (string.IsNullOrEmpty(args.Stack))
        throw new ArgumentException("Must provide stack name when deploying.", nameof(args.Stack));

      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} stack deploy {options} {args.Stack}").Execute();
    }

    /// <summary>
    /// Lists services in a stack using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> StackServicesCommand(this DockerUri host, StackServicesCommandArgs args)
    {
      if (string.IsNullOrEmpty(args.Stack))
        throw new ArgumentException("Must provide stack name.", nameof(args.Stack));

      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} stack services {options} {args.Stack}").Execute();
    }

    #endregion

    #region Existing methods (backward compatible)
    public static CommandResponse<IList<StackLsResponse>> StackLs(this DockerUri host,
      Orchestrator orchestrator = Orchestrator.All,
      bool kubeAllNamespaces = true,
      string kubeNamespace = null,
      string kubeConfigFile = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}";

      if (kubeAllNamespaces)
        opts += " --all-namespaces";
      if (null != kubeNamespace)
        opts += $" --namespace={kubeNamespace}";
      if (null != kubeConfigFile)
        opts += $" --kubeconfig={kubeConfigFile}";

      opts += " --format=\"{{.Name}};{{.Services}};{{.Orchestrator}};{{.Namespace}}\"";
      return
        new ProcessExecutor<StackLsResponseParser, IList<StackLsResponse>>(
          "docker".ResolveBinary(),
          $"{args} stack ls {opts}").Execute();
    }

    public static CommandResponse<IList<StackPsResponse>> StackPs(this DockerUri host,
      string stack,
      Orchestrator orchestrator = Orchestrator.All,
      string kubeNamespace = null,
      string kubeConfigFile = null,
      string filter = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}";

      if (null != kubeNamespace)
        opts += $" --namespace={kubeNamespace}";
      if (null != kubeConfigFile)
        opts += $" --kubeconfig={kubeConfigFile}";
      if (null != filter)
        opts += $" --filter={filter}";

      opts +=
        " --no-trunc --format=\"{{.ID}};{{.Name}};{{.Image}};{{.Node}};{{.DesiredState}};{{.CurrentState}};{{.Error}};{{.Ports}}\"";
      return
        new ProcessExecutor<StackPsResponseParser, IList<StackPsResponse>>(
          "docker".ResolveBinary(),
          $"{args} stack ps {opts} {stack}").Execute();
    }

    public static CommandResponse<IList<string>> StackRm(this DockerUri host,
      Orchestrator orchestrator = Orchestrator.All,
      string kubeConfigFile = null,
      ICertificatePaths certificates = null, params string[] stacks)
    {
      if (null == stacks || 0 == stacks.Length)
        throw new ArgumentException("Must provide with stacks when doing rm.", nameof(stacks));

      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}";

      if (null != kubeConfigFile)
        opts += $" --kubeconfig={kubeConfigFile}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} stack rm {opts} {string.Join(" ", stacks)}").Execute();
    }

    #endregion
  }
}
