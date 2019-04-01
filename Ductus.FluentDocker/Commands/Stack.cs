using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Commands
{
  public static class Stack
  {
    // TODO: https://docs.docker.com/engine/reference/commandline/stack_ps/
    public enum Orchestrator
    {
      All,
      Swarm,
      Kubernetes
    }

    public static CommandResponse<IList<string>> StackLs(this DockerUri host,
      Orchestrator orchestrator = Orchestrator.All,
      bool kubeAllNamespaces = true,
      string kubeNamespace = null,
      string kubeConfigFile = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}";

      if (kubeAllNamespaces) opts += " --all-namespaces";
      if (null != kubeNamespace) opts += $" --namespace={kubeNamespace}";
      if (null != kubeConfigFile) opts += $" --kubeconfig={kubeConfigFile}";

      opts += " --format=\"{{.Name}};{{.Services}};{{.Orchestrator}};{{.Namespace}}\"";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} stack ls {opts}").Execute();
    }

    public static CommandResponse<IList<string>> StackPs(this DockerUri host,
      string stack,
      Orchestrator orchestrator = Orchestrator.All,
      string kubeNamespace = null,
      string kubeConfigFile = null,
      string filter = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}";

      if (null != kubeNamespace) opts += $" --namespace={kubeNamespace}";
      if (null != kubeConfigFile) opts += $" --kubeconfig={kubeConfigFile}";
      if (null != filter) opts += $" --filter={filter}";

      opts +=
        " --no-trunc --format=\"{{.ID}};{{.Name}};{{.Image}};{{.Node}};{{.DesiredState}};{{.CurrentState}};{{.Error}};{{.Ports}}\"";
      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} stack ps {opts} {stack}").Execute();
    }

    public static CommandResponse<IList<string>> StackRm(this DockerUri host,
      Orchestrator orchestrator = Orchestrator.All,
      string kubeConfigFile = null,
      ICertificatePaths certificates = null, params string []stacks)
    {
      if (null == stacks || 0 == stacks.Length)
        throw new ArgumentException("Must provide with stacks when doing rm.",nameof(stacks));
      
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}";

      if (null != kubeConfigFile) opts += $" --kubeconfig={kubeConfigFile}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} stack rm {opts} {string.Join(" ", stacks)}").Execute();
    }
  }
}