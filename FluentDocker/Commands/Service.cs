using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Stacks;

namespace Ductus.FluentDocker.Commands
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
