using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  public partial class DockerCliComposeDriver
  {
    private async Task<IList<ComposeProcesses>> ParseTopOutputWithServiceInfoAsync(
        DriverContext context,
        ComposeFileConfig config,
        string topOutput,
        CancellationToken cancellationToken)
    {
      // ponytail: no containers -> no join needed; skip the extra compose ps spawn on the hot path.
      if (string.IsNullOrWhiteSpace(topOutput))
        return ParseTopOutput(topOutput);

      try
      {
        var args = BuildComposeArgs(config) + " " + BuildListSubArgs(new ComposeListConfig());
        var result = await ExecuteCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
          if (Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Compose ps join for top failed: {Error}", ErrorOrDefault(result, "Compose ps failed"));
          return ParseTopOutput(topOutput);
        }

        if (!TryParseServiceList(result.Output, Logger, out var services, out var parseError))
        {
          if (Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Compose ps join for top parse failed: {Error}", parseError);
          return ParseTopOutput(topOutput);
        }

        var containersByName = new Dictionary<string, ComposeServiceInfo>(StringComparer.Ordinal);
        foreach (var service in services)
        {
          if (!string.IsNullOrEmpty(service.ContainerName))
            containersByName[service.ContainerName] = service;
        }

        return ParseTopOutput(topOutput, containersByName);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogDebug(ex, "Compose ps join for top threw");
        return ParseTopOutput(topOutput);
      }
    }
  }
}
