using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI container driver - inspection, listing, logs, stats, and query operations.
  /// </summary>
  public partial class DockerCliContainerDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    private static readonly char[] SpaceSeparator = [' '];
    private static readonly string[] SlashSeparator = [" / "];
    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Container>> InspectAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"inspect {containerId}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<Container>.Fail(
              result.Error ?? "Container inspect failed",
              ErrorCodes.Container.InspectFailed,
              CreateErrorContext(context, "InspectContainer", result),
              result.ExitCode);
        }

        var containers = JsonSerializer.Deserialize<List<Container>>(result.Output, JsonHelper.CaseInsensitiveOptions);
        var container = containers?.FirstOrDefault();

        if (container == null)
        {
          return CommandResponse<Container>.Fail(
              $"Container {containerId} not found",
              ErrorCodes.Container.NotFound);
        }

        return CommandResponse<Container>.Ok(container);
      }
      catch (Exception ex)
      {
        return CommandResponse<Container>.Fail(ex.Message, ErrorCodes.Container.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context,
        ContainerListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "ps --format \"{{json .}}\"";

        if (filter?.All == true)
          args += " -a";

        // Add label filters
        if (filter?.Labels != null && filter.Labels.Count > 0)
        {
          foreach (var label in filter.Labels)
          {
            if (string.IsNullOrEmpty(label.Value))
              args += $" --filter \"label={label.Key}\"";
            else
              args += $" --filter \"label={label.Key}={label.Value}\"";
          }
        }

        // Add name filter
        if (!string.IsNullOrEmpty(filter?.Name))
          args += $" --filter \"name={filter.Name}\"";

        // Add status filter
        if (!string.IsNullOrEmpty(filter?.Status))
          args += $" --filter \"status={filter.Status}\"";

        // Add ID filter
        if (!string.IsNullOrEmpty(filter?.Id))
          args += $" --filter \"id={filter.Id}\"";

        // Add ancestor filter
        if (!string.IsNullOrEmpty(filter?.Ancestor))
          args += $" --filter \"ancestor={filter.Ancestor}\"";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<Container>>.Fail(
              result.Error ?? "Container list failed",
              ErrorCodes.General.Unknown);
        }

        var containers = new List<Container>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
          try
          {
            // Docker ps JSON has different field names than our Container model
            var dto = JsonSerializer.Deserialize<DockerPsDto>(line, JsonHelper.CaseInsensitiveOptions);
            if (dto != null)
            {
              var container = new Container
              {
                Id = dto.ID,
                Image = dto.Image,
                Name = dto.Names
              };

              // Parse CreatedAt if present
              if (!string.IsNullOrEmpty(dto.CreatedAt) && DateTime.TryParse(dto.CreatedAt, out var created))
              {
                container.Created = created;
              }

              // Parse State if present
              if (!string.IsNullOrEmpty(dto.State))
              {
                container.State = new ContainerState
                {
                  Running = dto.State.Equals("running", StringComparison.OrdinalIgnoreCase),
                  Status = dto.Status
                };
              }

              containers.Add(container);
            }
          }
          catch (Exception ex)
          {
            Logger.Log($"Container inspect JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<Container>>.Ok(containers);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Container>>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        string containerId,
        bool follow = false,
        int? tail = null,
        bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "logs";
        if (follow)
          args += " -f";
        if (tail.HasValue)
          args += $" --tail {tail.Value}";
        if (timestamps)
          args += " -t";
        args += $" {containerId}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              result.Error ?? "Get logs failed",
              ErrorCodes.Container.LogsFailed);
        }

        // docker logs writes to both stdout and stderr.
        // Combine both to capture all container output.
        var logs = !string.IsNullOrEmpty(result.Error)
            ? result.Output + result.Error
            : result.Output;
        return CommandResponse<string>.Ok(logs);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context,
        string containerId,
        string psOptions = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"top {containerId}";
        if (!string.IsNullOrEmpty(psOptions))
          args += $" {psOptions}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ContainerProcesses>.Fail(
              result.Error ?? "Container top failed",
              ErrorCodes.Container.TopFailed,
              CreateErrorContext(context, "TopContainer", result),
              result.ExitCode);
        }

        var processes = new ContainerProcesses();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
        {
          processes.Titles = lines[0].Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
          for (var i = 1; i < lines.Length; i++)
          {
            processes.Processes.Add(lines[i].Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries).ToList());
          }
        }

        return CommandResponse<ContainerProcesses>.Ok(processes);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerProcesses>.Fail(ex.Message, ErrorCodes.Container.TopFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"diff {containerId}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<FilesystemChange>>.Fail(
              result.Error ?? "Container diff failed",
              ErrorCodes.Container.DiffFailed,
              CreateErrorContext(context, "DiffContainer", result),
              result.ExitCode);
        }

        var changes = new List<FilesystemChange>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          if (line.Length > 2)
          {
            changes.Add(new FilesystemChange
            {
              Kind = line.Substring(0, 1),
              Path = line.Substring(2)
            });
          }
        }

        return CommandResponse<IList<FilesystemChange>>.Ok(changes);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<FilesystemChange>>.Fail(ex.Message, ErrorCodes.Container.DiffFailed);
      }
    }

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
            $"stats --no-stream --format \"{{{{json .}}}}\" {containerId}",
            cancellationToken);

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

    #endregion

    #region Stats Parsing Helpers

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
          stats.CpuPercent = ParsePercent(cpuPerc.GetString());

        if (root.TryGetProperty("MemPerc", out var memPerc))
          stats.MemoryPercent = ParsePercent(memPerc.GetString());

        if (root.TryGetProperty("MemUsage", out var memUsage))
        {
          var (usage, limit) = ParseMemoryUsage(memUsage.GetString());
          stats.MemoryUsage = usage;
          stats.MemoryLimit = limit;
        }

        if (root.TryGetProperty("NetIO", out var netIO))
        {
          var (rx, tx) = ParseIOPair(netIO.GetString());
          stats.NetworkRxBytes = rx;
          stats.NetworkTxBytes = tx;
        }

        if (root.TryGetProperty("BlockIO", out var blockIO))
        {
          var (read, write) = ParseIOPair(blockIO.GetString());
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

    private static double ParsePercent(string value)
    {
      if (string.IsNullOrEmpty(value))
        return 0;
      value = value.TrimEnd('%');
      return double.TryParse(value, System.Globalization.NumberStyles.Float,
          System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static (long usage, long limit) ParseMemoryUsage(string value)
    {
      // Format: "1.5MiB / 7.8GiB" or "1500000B / 8000000000B"
      if (string.IsNullOrEmpty(value))
        return (0, 0);

      var parts = value.Split(SlashSeparator, StringSplitOptions.None);
      if (parts.Length != 2)
        return (0, 0);

      return (ParseByteValue(parts[0].Trim()), ParseByteValue(parts[1].Trim()));
    }

    private static (long first, long second) ParseIOPair(string value)
    {
      // Format: "1.2kB / 0B" or "1200B / 0B"
      if (string.IsNullOrEmpty(value))
        return (0, 0);

      var parts = value.Split(SlashSeparator, StringSplitOptions.None);
      if (parts.Length != 2)
        return (0, 0);

      return (ParseByteValue(parts[0].Trim()), ParseByteValue(parts[1].Trim()));
    }

    private static long ParseByteValue(string value)
    {
      if (string.IsNullOrEmpty(value))
        return 0;

      // Handle various suffixes: B, kB, KB, MiB, MB, GiB, GB, TiB, TB
      double multiplier = 1;
      var numericPart = value;

      if (value.EndsWith("TiB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1024L * 1024 * 1024 * 1024;
        numericPart = value[..^3];
      }
      else if (value.EndsWith("TB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1000L * 1000 * 1000 * 1000;
        numericPart = value[..^2];
      }
      else if (value.EndsWith("GiB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1024L * 1024 * 1024;
        numericPart = value[..^3];
      }
      else if (value.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1000L * 1000 * 1000;
        numericPart = value[..^2];
      }
      else if (value.EndsWith("MiB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1024L * 1024;
        numericPart = value[..^3];
      }
      else if (value.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1000L * 1000;
        numericPart = value[..^2];
      }
      else if (value.EndsWith("KiB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1024;
        numericPart = value[..^3];
      }
      else if (value.EndsWith("kB", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
      {
        multiplier = 1000;
        numericPart = value[..^2];
      }
      else if (value.EndsWith("B", StringComparison.OrdinalIgnoreCase))
      {
        numericPart = value[..^1];
      }

      if (double.TryParse(numericPart, System.Globalization.NumberStyles.Float,
          System.Globalization.CultureInfo.InvariantCulture, out var number))
      {
        return (long)(number * multiplier);
      }

      return 0;
    }

    #endregion

    #region Helper Types

    /// <summary>DTO for docker ps JSON output.</summary>
    private sealed class DockerPsDto
    {
      public string ID { get; set; }
      public string Image { get; set; }
      public string Command { get; set; }
      public string CreatedAt { get; set; }
      public string Names { get; set; }
      public string State { get; set; }
      public string Status { get; set; }
      public string Ports { get; set; }
      public string Labels { get; set; }
      public string Mounts { get; set; }
      public string Networks { get; set; }
      public string RunningFor { get; set; }
      public string Size { get; set; }
    }

    #endregion
  }
}
