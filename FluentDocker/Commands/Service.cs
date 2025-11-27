using System;
using System.Collections.Generic;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Stacks;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker service commands
  /// </summary>
  /// <remarks>
  /// API 1.24+
  /// docker service create [OPTIONS] IMAGE [COMMAND] [ARG...]
  /// </remarks>
  public static class Service
  {
    // TODO: Implement me!
    public static CommandResponse<IList<string>> ServiceCreate(this DockerUri host,
      Orchestrator orchestrator = Orchestrator.All,
      string kubeConfigFile = null,
      ICertificatePaths certificates = null, params string[] stacks)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      var opts = $"--orchestrator={orchestrator}"; // TODO:

      return // TODO:
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{args} stack rm {opts} {string.Join(" ", stacks)}").Execute();
    }
  }
}
