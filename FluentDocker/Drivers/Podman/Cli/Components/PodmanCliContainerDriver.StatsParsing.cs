#nullable disable warnings
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
      var titles = lines[0].Split(WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries);
      processes.Titles = [.. titles];

      for (var i = 1; i < lines.Length; i++)
      {
        // ponytail: count-limited split keeps the last column (COMMAND) intact with its spaces.
        var fields = lines[i].Split(
            WhitespaceSeparators, titles.Length, StringSplitOptions.RemoveEmptyEntries);
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
    /// Handles both single object and JSON array formats, and both output shapes podman
    /// emits: the human/table form with string values (<c>"cpu_percent": "5.23%"</c>,
    /// <c>"mem_usage": "100MiB / 2GiB"</c>) and the Go-marshaled
    /// <c>define.ContainerStats</c> form of podman 4/5 with numeric values and Go field
    /// names (<c>CPU</c>, <c>MemUsage</c>/<c>MemLimit</c> in bytes,
    /// <c>NetInput</c>/<c>NetOutput</c>, <c>BlockInput</c>/<c>BlockOutput</c>,
    /// <c>PIDs</c>, plus the podman 5 per-interface <c>Network</c> map).
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

        // CPU/memory percentages: table form carries "5.23%" strings; the Go form carries
        // numeric CPU / MemPerc fields (already percentages).
        var cpuPercent = !string.IsNullOrEmpty(cpuStr)
            ? ParsePercent(cpuStr)
            : token.GetDoubleOrDefault("CPU", token.GetDoubleOrDefault("AvgCPU"));
        var memPercent = !string.IsNullOrEmpty(memPercStr)
            ? ParsePercent(memPercStr)
            : token.GetDoubleOrDefault("MemPerc");

        long memUsage, memLimit;
        if (!string.IsNullOrEmpty(memUsageStr))
        {
          (memUsage, memLimit) = ParseMemoryUsage(memUsageStr);
        }
        else
        {
          // Go-marshaled form: MemUsage/MemLimit are raw byte counts.
          memUsage = token.GetInt64OrDefault("MemUsage");
          memLimit = token.GetInt64OrDefault("MemLimit", token.GetInt64OrDefault("mem_limit"));
        }

        long netRx, netTx;
        if (!string.IsNullOrEmpty(netIoStr))
        {
          (netRx, netTx) = ParseIOPair(netIoStr);
        }
        else
        {
          netRx = GetByteCountOrDefault(token, "NetInput", "net_input");
          netTx = GetByteCountOrDefault(token, "NetOutput", "net_output");
          if (netRx == 0 && netTx == 0)
            (netRx, netTx) = SumNetworkInterfaceStats(token);
        }

        long blockRead, blockWrite;
        if (!string.IsNullOrEmpty(blockIoStr))
        {
          (blockRead, blockWrite) = ParseIOPair(blockIoStr);
        }
        else
        {
          blockRead = GetByteCountOrDefault(token, "BlockInput", "block_input");
          blockWrite = GetByteCountOrDefault(token, "BlockOutput", "block_output");
        }

        var pids = 0;
        if (!string.IsNullOrEmpty(pidsStr))
          int.TryParse(pidsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out pids);
        else
          pids = token.GetInt32OrDefault("PIDs", token.GetInt32OrDefault("pids"));

        return new ContainerStatsResult
        {
          ContainerId = token.GetStringOrDefault("id")
                          ?? token.GetStringOrDefault("ContainerID")
                          ?? token.GetStringOrDefault("container_id"),
          Name = token.GetStringOrDefault("Name")
                   ?? token.GetStringOrDefault("name"),
          CpuPercent = cpuPercent,
          MemoryUsage = memUsage,
          MemoryLimit = memLimit,
          MemoryPercent = memPercent,
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
          || token.Prop("MemUsage") != null
          || token.Prop("CPU") != null
          || token.Prop("AvgCPU") != null
          || token.Prop("MemPerc") != null
          || token.Prop("MemLimit") != null
          || token.Prop("PIDs") != null;
    }

    /// <summary>
    /// Reads a byte-count field that podman emits either as a suffixed string
    /// (<c>"1.5kB"</c>, table form) or as a raw numeric byte count (Go-marshaled form),
    /// probing the Go spelling first and then the lowercase table spelling.
    /// </summary>
    private static long GetByteCountOrDefault(JsonElement token, string goName, string tableName)
    {
      var text = token.GetStringOrDefault(goName) ?? token.GetStringOrDefault(tableName);
      if (!string.IsNullOrEmpty(text))
        return ParseByteValue(text);
      return token.GetInt64OrDefault(goName, token.GetInt64OrDefault(tableName));
    }

    /// <summary>
    /// Sums per-interface RxBytes/TxBytes from the podman 5 <c>Network</c> map
    /// (<c>{"eth0": {"RxBytes": …, "TxBytes": …}, …}</c>). Returns zeros when the
    /// property is absent or not an object.
    /// </summary>
    private static (long rx, long tx) SumNetworkInterfaceStats(JsonElement token)
    {
      var network = token.Prop("Network") ?? token.Prop("network");
      if (network?.ValueKind != JsonValueKind.Object)
        return (0, 0);

      long rx = 0, tx = 0;
      foreach (var iface in network.Value.EnumerateObject())
      {
        if (iface.Value.ValueKind != JsonValueKind.Object)
          continue;
        rx += iface.Value.GetInt64OrDefault("RxBytes");
        tx += iface.Value.GetInt64OrDefault("TxBytes");
      }

      return (rx, tx);
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
