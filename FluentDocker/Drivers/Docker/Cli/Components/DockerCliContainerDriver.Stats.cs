using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

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
        var result = await ExecuteCommandAsync(
            $"stats --no-stream --format \"{{{{json .}}}}\" {QuoteArgumentIfNeeded(containerId)}",
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerStatsResult>.Fail(
              result.Error ?? "Container stats failed",
              ErrorCodes.Container.StatsFailed,
              CreateErrorContext(context, "StatsContainer", result),
              result.ExitCode);
        }

        var stats = ParseStatsOutput(result.Output, containerId);
        return CommandResponse<ContainerStatsResult>.Ok(stats);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerStatsResult>.Fail(ex.Message, ErrorCodes.Container.StatsFailed);
      }
    }

    #region Stats Parsing

    private static ContainerStatsResult ParseStatsOutput(string output, string containerId)
    {
      var stats = new ContainerStatsResult { ContainerId = containerId };

      try
      {
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
          if (int.TryParse(pids.GetString(), out var pidCount))
            stats.Pids = pidCount;
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Container stats JSON parsing failed: {ex.Message}");
      }

      return stats;
    }

    #endregion
  }
}
