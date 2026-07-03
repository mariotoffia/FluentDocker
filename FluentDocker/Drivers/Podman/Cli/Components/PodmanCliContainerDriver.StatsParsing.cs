using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI container driver — output parsing. Split into its own partial file purely to
  /// keep each source file within the repository's 500-line limit.
  /// </summary>
  public partial class PodmanCliContainerDriver
  {
    #region Output Parsing

    private static ContainerProcesses ParseTopOutput(string output)
    {
      var processes = new ContainerProcesses();
      if (string.IsNullOrWhiteSpace(output))
        return processes;

      var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      if (lines.Length == 0)
        return processes;

      // First line is header
      processes.Titles = [.. lines[0].Split(
          WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries)];

      for (var i = 1; i < lines.Length; i++)
      {
        var fields = lines[i].Split(
            WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries);
        processes.Processes.Add([.. fields]);
      }

      return processes;
    }

    private static List<FilesystemChange> ParseDiffOutput(string output)
    {
      var changes = new List<FilesystemChange>();
      if (string.IsNullOrWhiteSpace(output))
        return changes;

      foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
      {
        var trimmed = line.Trim();
        if (trimmed.Length < 2)
          continue;

        changes.Add(new FilesystemChange
        {
          Kind = trimmed[0].ToString(),
          Path = trimmed[2..].Trim()
        });
      }

      return changes;
    }

    /// <summary>
    /// Parses Podman stats JSON output into a <see cref="ContainerStatsResult"/>.
    /// Handles both single object and JSON array formats, plus alternate lowercase keys.
    /// Empty/whitespace output is a legitimately empty result; non-empty but unparseable
    /// output throws a <see cref="FluentDockerException"/> with diagnostics rather than
    /// silently yielding a zeroed result.
    /// </summary>
    public static ContainerStatsResult ParseStatsOutput(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return new ContainerStatsResult();

      try
      {
        var trimmed = json.Trim();
        JsonElement token;

        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          var enumerator = root.EnumerateArray();
          if (!enumerator.MoveNext())
            return new ContainerStatsResult(); // empty array is a legitimately empty result
          token = enumerator.Current;
        }
        else
        {
          token = JsonHelper.ParseElement(trimmed);
        }

        if (!HasStatsFields(token))
          throw new FormatException("no recognized Podman stats fields were present");

        var cpuStr = token.GetStringOrDefault("cpu_percent")
                     ?? token.GetStringOrDefault("CPUPerc")
                     ?? token.GetStringOrDefault("cpu_perc");
        var memUsageStr = token.GetStringOrDefault("MemUsage")
                          ?? token.GetStringOrDefault("mem_usage");
        var memPercStr = token.GetStringOrDefault("mem_percent")
                        ?? token.GetStringOrDefault("MemPerc")
                         ?? token.GetStringOrDefault("mem_perc");
        var netIoStr = token.GetStringOrDefault("NetIO")
                       ?? token.GetStringOrDefault("net_io");
        var blockIoStr = token.GetStringOrDefault("BlockIO")
                         ?? token.GetStringOrDefault("block_io");
        var pidsStr = token.GetStringOrDefault("PIDs")
                      ?? token.GetStringOrDefault("pids");

        var (memUsage, memLimit) = ParseMemoryUsage(memUsageStr);
        var (netRx, netTx) = string.IsNullOrEmpty(netIoStr)
            ? (ParseByteValue(token.GetStringOrDefault("net_input")),
               ParseByteValue(token.GetStringOrDefault("net_output")))
            : ParseIOPair(netIoStr);
        var (blockRead, blockWrite) = string.IsNullOrEmpty(blockIoStr)
            ? (ParseByteValue(token.GetStringOrDefault("block_input")),
               ParseByteValue(token.GetStringOrDefault("block_output")))
            : ParseIOPair(blockIoStr);

        int.TryParse(pidsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pids);

        return new ContainerStatsResult
        {
          ContainerId = token.GetStringOrDefault("id")
                          ?? token.GetStringOrDefault("ContainerID")
                          ?? token.GetStringOrDefault("container_id"),
          Name = token.GetStringOrDefault("Name")
                   ?? token.GetStringOrDefault("name"),
          CpuPercent = ParsePercent(cpuStr),
          MemoryUsage = memUsage,
          MemoryLimit = memLimit,
          MemoryPercent = ParsePercent(memPercStr),
          NetworkRxBytes = netRx,
          NetworkTxBytes = netTx,
          BlockReadBytes = blockRead,
          BlockWriteBytes = blockWrite,
          Pids = pids
        };
      }
      catch (Exception ex)
      {
        // Non-empty but unparseable output is a real failure — surface it with diagnostics
        // instead of returning a zeroed ContainerStatsResult that masks the problem.
        throw new FluentDockerException(
            $"Failed to parse Podman container stats output: {ex.Message}");
      }
    }

    private static bool HasStatsFields(JsonElement token)
    {
      return token.Prop("id") != null
          || token.Prop("ContainerID") != null
          || token.Prop("container_id") != null
          || token.Prop("cpu_percent") != null
          || token.Prop("CPUPerc") != null
          || token.Prop("cpu_perc") != null
          || token.Prop("mem_usage") != null
          || token.Prop("MemUsage") != null;
    }

    /// <summary>Parses a percentage string. Delegates to <see cref="CliOutputParser"/>.</summary>
    public static double ParsePercent(string value) => CliOutputParser.ParsePercent(value);

    /// <summary>Parses a memory usage string. Delegates to <see cref="CliOutputParser"/>.</summary>
    public static (long usage, long limit) ParseMemoryUsage(string value) => CliOutputParser.ParseMemoryUsage(value);

    /// <summary>Parses an I/O pair string. Delegates to <see cref="CliOutputParser"/>.</summary>
    public static (long first, long second) ParseIOPair(string value) => CliOutputParser.ParseIOPair(value);

    /// <summary>Parses a byte value string with suffix. Delegates to <see cref="CliOutputParser"/>.</summary>
    public static long ParseByteValue(string value) => CliOutputParser.ParseByteValue(value);

    #endregion
  }
}
