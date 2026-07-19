#nullable disable warnings
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI container driver — stats operation and parsing helpers.
  /// </summary>
  public partial class DockerCliContainerDriver
  {
    /// <inheritdoc />
    public async Task<CommandResponse<ContainerStatsResult>> StatsAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // Use --no-stream to get a single snapshot instead of continuous stream
        // Use --format with JSON output for easier parsing
        var result = await ExecuteCommandAsync(context,
            $"stats --no-stream --format \"{{{{json .}}}}\" {QuotePositionalArgument(containerId, nameof(containerId))}",
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerStatsResult>.Fail(
              ErrorOrDefault(result, "Container stats failed"),
              FailureCode(result.Error, ErrorCodes.Container.StatsFailed),
              CreateErrorContext(context, "StatsContainer", result),
              result.ExitCode);
        }

        var stats = ParseStatsOutput(result.Output, containerId, Logger);
        if (stats == null)
        {
          return CommandResponse<ContainerStatsResult>.Fail(
              "Container stats output could not be parsed",
              ErrorCodes.Container.StatsFailed,
              CreateErrorContext(context, "StatsContainer", result),
              result.ExitCode);
        }
        return CommandResponse<ContainerStatsResult>.Ok(stats);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerStatsResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.StatsFailed));
      }
    }

    #region Stats Parsing

    private static ContainerStatsResult ParseStatsOutput(string output, string containerId, ILogger logger = null)
    {
      logger ??= NullLogger.Instance;
      var stats = new ContainerStatsResult { ContainerId = containerId };

      try
      {
        if (string.IsNullOrWhiteSpace(output))
          return null;
        using var json = JsonDocument.Parse(output.Trim());
        var root = json.RootElement;

        if (root.TryGetProperty("Name", out var name))
          stats.Name = name.GetString();

        if (root.TryGetProperty("CPUPerc", out var cpuPerc))
          stats.CpuPercent = CliOutputParser.ParsePercent(cpuPerc.GetString());

        if (root.TryGetProperty("MemPerc", out var memPerc))
          stats.MemoryPercent = CliOutputParser.ParsePercent(memPerc.GetString());

        if (root.TryGetProperty("MemUsage", out var memUsage))
        {
          var (usage, limit) = CliOutputParser.ParseMemoryUsage(memUsage.GetString());
          stats.MemoryUsage = usage;
          stats.MemoryLimit = limit;
        }

        if (root.TryGetProperty("NetIO", out var netIO))
        {
          var (rx, tx) = CliOutputParser.ParseIOPair(netIO.GetString());
          stats.NetworkRxBytes = rx;
          stats.NetworkTxBytes = tx;
        }

        if (root.TryGetProperty("BlockIO", out var blockIO))
        {
          var (read, write) = CliOutputParser.ParseIOPair(blockIO.GetString());
          stats.BlockReadBytes = read;
          stats.BlockWriteBytes = write;
        }

        if (root.TryGetProperty("PIDs", out var pids))
        {
          if (int.TryParse(pids.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pidCount))
            stats.Pids = pidCount;
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Container stats JSON parsing failed");
        return null;
      }

      return stats;
    }

    #endregion
  }
}
