using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI container driver - execution, copy, and monitoring operations.
  /// </summary>
  public partial class PodmanCliContainerDriver
  {
    #region Information Operations (continued)

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string containerId,
        bool follow = false, int? tail = null, bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "logs";
        if (follow)
          args += " --follow";
        if (tail.HasValue)
          args += $" --tail {tail.Value}";
        if (timestamps)
          args += " --timestamps";
        args += $" {containerId}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              result.Error ?? "Get logs failed", ErrorCodes.General.Unknown);

        return CommandResponse<string>.Ok(result.Output);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context, string containerId, string psOptions = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"top {containerId}";
        if (!string.IsNullOrEmpty(psOptions))
          args += $" {psOptions}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<ContainerProcesses>.Fail(
              result.Error ?? "Container top failed", ErrorCodes.Container.TopFailed);

        var processes = ParseTopOutput(result.Output);
        return CommandResponse<ContainerProcesses>.Ok(processes);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerProcesses>.Fail(
            ex.Message, ErrorCodes.Container.TopFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"diff {containerId}", cancellationToken);
        if (!result.Success)
          return CommandResponse<IList<FilesystemChange>>.Fail(
              result.Error ?? "Container diff failed", ErrorCodes.Container.DiffFailed);

        var changes = ParseDiffOutput(result.Output);
        return CommandResponse<IList<FilesystemChange>>.Ok(changes);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<FilesystemChange>>.Fail(
            ex.Message, ErrorCodes.Container.DiffFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerStatsResult>> StatsAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"stats --no-stream --format json {containerId}", cancellationToken);
        if (!result.Success)
          return CommandResponse<ContainerStatsResult>.Fail(
              result.Error ?? "Container stats failed", ErrorCodes.Container.StatsFailed);

        var stats = ParseStatsOutput(result.Output);
        return CommandResponse<ContainerStatsResult>.Ok(stats);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerStatsResult>.Fail(
            ex.Message, ErrorCodes.Container.StatsFailed);
      }
    }

    #endregion

    #region Execution Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ExecResult>> ExecAsync(
        DriverContext context, string containerId, ExecConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "exec";
        if (config.Detach)
          args += " -d";
        if (config.Tty)
          args += " -t";
        if (config.Interactive)
          args += " -i";
        if (config.Privileged)
          args += " --privileged";
        if (!string.IsNullOrEmpty(config.User))
          args += $" --user {config.User}";
        if (!string.IsNullOrEmpty(config.WorkingDir))
          args += $" -w {config.WorkingDir}";

        if (config.Environment != null)
          foreach (var env in config.Environment)
            args += $" -e {env.Key}={env.Value}";

        args += $" {containerId}";

        if (config.Command != null)
          foreach (var cmd in config.Command)
            args += $" {QuoteArgumentIfNeeded(cmd)}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return CommandResponse<ExecResult>.Ok(new ExecResult
        {
          ExitCode = result.ExitCode,
          StdOut = result.Output,
          StdErr = result.Error
        });
      }
      catch (Exception ex)
      {
        return CommandResponse<ExecResult>.Fail(
            ex.Message, ErrorCodes.Container.ExecFailed);
      }
    }

    #endregion

    #region Copy Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyToAsync(
        DriverContext context, string containerId,
        string hostPath, string containerPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"cp \"{hostPath}\" \"{containerId}:{containerPath}\"", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Copy to container failed", ErrorCodes.Container.CopyFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyFromAsync(
        DriverContext context, string containerId,
        string containerPath, string hostPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"cp \"{containerId}:{containerPath}\" \"{hostPath}\"", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Copy from container failed", ErrorCodes.Container.CopyFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
      }
    }

    #endregion

    #region Export/Update Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ExportAsync(
        DriverContext context, string containerId, string outputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"export -o \"{outputPath}\" {containerId}", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Container export failed", ErrorCodes.Container.ExportFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.ExportFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RenameAsync(
        DriverContext context, string containerId, string newName,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"rename {containerId} {newName}", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Container rename failed", ErrorCodes.Container.RenameFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RenameFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context, string containerId, ContainerUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"update";
        if (config.MemoryLimit.HasValue)
          args += $" --memory {config.MemoryLimit.Value}";
        if (config.MemoryReservation.HasValue)
          args += $" --memory-reservation {config.MemoryReservation.Value}";
        if (config.CpuShares.HasValue)
          args += $" --cpu-shares {config.CpuShares.Value}";
        if (config.CpuPeriod.HasValue)
          args += $" --cpu-period {config.CpuPeriod.Value}";
        if (config.CpuQuota.HasValue)
          args += $" --cpu-quota {config.CpuQuota.Value}";
        if (!string.IsNullOrEmpty(config.CpusetCpus))
          args += $" --cpuset-cpus {config.CpusetCpus}";
        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args += $" --restart {config.RestartPolicy}";
        if (config.PidsLimit.HasValue)
          args += $" --pids-limit {config.PidsLimit.Value}";

        args += $" {containerId}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Container update failed", ErrorCodes.Container.UpdateFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UpdateFailed);
      }
    }

    #endregion

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
      processes.Titles = new List<string>(lines[0].Split(
          new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

      for (var i = 1; i < lines.Length; i++)
      {
        var fields = lines[i].Split(
            new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        processes.Processes.Add(new List<string>(fields));
      }

      return processes;
    }

    private static IList<FilesystemChange> ParseDiffOutput(string output)
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
          Path = trimmed.Substring(2).Trim()
        });
      }

      return changes;
    }

    /// <summary>
    /// Parses Podman stats JSON output into a <see cref="ContainerStatsResult"/>.
    /// Handles both single object and JSON array formats, plus alternate lowercase keys.
    /// </summary>
    public static ContainerStatsResult ParseStatsOutput(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return new ContainerStatsResult();

      try
      {
        var trimmed = json.Trim();
        JToken token;

        if (trimmed.StartsWith("["))
          token = JArray.Parse(trimmed).First;
        else
          token = JObject.Parse(trimmed);

        var cpuStr = token["CPUPerc"]?.Value<string>()
                     ?? token["cpu_perc"]?.Value<string>();
        var memUsageStr = token["MemUsage"]?.Value<string>()
                          ?? token["mem_usage"]?.Value<string>();
        var memPercStr = token["MemPerc"]?.Value<string>()
                         ?? token["mem_perc"]?.Value<string>();
        var netIoStr = token["NetIO"]?.Value<string>()
                       ?? token["net_io"]?.Value<string>();
        var blockIoStr = token["BlockIO"]?.Value<string>()
                         ?? token["block_io"]?.Value<string>();
        var pidsStr = token["PIDs"]?.Value<string>()
                      ?? token["pids"]?.Value<string>();

        var (memUsage, memLimit) = ParseMemoryUsage(memUsageStr);
        var (netRx, netTx) = ParseIOPair(netIoStr);
        var (blockRead, blockWrite) = ParseIOPair(blockIoStr);

        int.TryParse(pidsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pids);

        return new ContainerStatsResult
        {
          ContainerId = token["ContainerID"]?.Value<string>()
                          ?? token["container_id"]?.Value<string>(),
          Name = token["Name"]?.Value<string>()
                   ?? token["name"]?.Value<string>(),
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
      catch
      {
        return new ContainerStatsResult();
      }
    }

    /// <summary>
    /// Parses a percentage string (e.g. "5.23%") into a double. Returns 0 on failure.
    /// </summary>
    public static double ParsePercent(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;
      var clean = value.TrimEnd('%').Trim();
      return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
          ? result : 0;
    }

    /// <summary>
    /// Parses a memory usage string (e.g. "100MiB / 2GiB") into (usage, limit) in bytes.
    /// </summary>
    public static (long usage, long limit) ParseMemoryUsage(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return (0, 0);
      var parts = value.Split(new[] { " / " }, 2, StringSplitOptions.None);
      var usage = parts.Length > 0 ? ParseByteValue(parts[0].Trim()) : 0;
      var limit = parts.Length > 1 ? ParseByteValue(parts[1].Trim()) : 0;
      return (usage, limit);
    }

    /// <summary>
    /// Parses an I/O pair string (e.g. "1.5kB / 2.3kB") into (first, second) in bytes.
    /// </summary>
    public static (long first, long second) ParseIOPair(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return (0, 0);
      var parts = value.Split(new[] { " / " }, 2, StringSplitOptions.None);
      var first = parts.Length > 0 ? ParseByteValue(parts[0].Trim()) : 0;
      var second = parts.Length > 1 ? ParseByteValue(parts[1].Trim()) : 0;
      return (first, second);
    }

    /// <summary>
    /// Parses a byte value string with suffix (e.g. "1.5kB", "100MiB", "2GiB").
    /// Uses base-1000 for kB/MB/GB/TB and base-1024 for KiB/MiB/GiB/TiB.
    /// </summary>
    public static long ParseByteValue(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;
      var s = value.Trim();

      // Order matters: check longer suffixes first to avoid partial matches.
      var suffixes = new (string suffix, double multiplier)[]
      {
                ("TiB", 1024.0 * 1024 * 1024 * 1024),
                ("GiB", 1024.0 * 1024 * 1024),
                ("MiB", 1024.0 * 1024),
                ("KiB", 1024.0),
                ("TB", 1000.0 * 1000 * 1000 * 1000),
                ("GB", 1000.0 * 1000 * 1000),
                ("MB", 1000.0 * 1000),
                ("kB", 1000.0),
                ("KB", 1000.0),
                ("B", 1.0)
      };

      foreach (var (suffix, multiplier) in suffixes)
      {
        if (!s.EndsWith(suffix, StringComparison.Ordinal))
          continue;

        var numStr = s.Substring(0, s.Length - suffix.Length).Trim();
        if (double.TryParse(numStr, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var num))
          return (long)(num * multiplier);
        return 0;
      }

      // No suffix: try parsing as raw number.
      return double.TryParse(s, NumberStyles.Float,
                 CultureInfo.InvariantCulture, out var raw)
          ? (long)raw : 0;
    }

    /// <summary>Quotes an argument if it contains whitespace or shell metacharacters.</summary>
    private static string QuoteArgumentIfNeeded(string arg)
    {
      if (string.IsNullOrEmpty(arg)) return "\"\"";
      if (arg.IndexOfAny(new[] { ' ', '\t', ';', '&', '|', '>', '<', '"', '\'' }) < 0) return arg;
      return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
    #endregion
  }
}
