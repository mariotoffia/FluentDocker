using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IStreamDriver.
  /// </summary>
  public class DockerCliStreamDriver : DockerCliDriverBase, IStreamDriver
  {
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliStreamDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <summary>
    /// Builds the CLI arguments string for streaming container logs.
    /// </summary>
    /// <param name="containerId">Container ID or name.</param>
    /// <param name="config">Stream logs configuration (null uses defaults).</param>
    /// <returns>The CLI arguments string.</returns>
    public static string BuildStreamLogsArgs(string containerId, StreamLogsConfig config)
    {
      config ??= new StreamLogsConfig();
      var args = "logs";
      if (config.Follow)
        args += " -f";
      if (config.Timestamps)
        args += " -t";
      if (config.Tail.HasValue)
        args += $" --tail {config.Tail.Value}";
      if (!string.IsNullOrEmpty(config.Since))
        args += $" --since {config.Since}";
      if (!string.IsNullOrEmpty(config.Until))
        args += $" --until {config.Until}";
      if (config.Details)
        args += " --details";
      args += $" {containerId}";
      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context,
        string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamLogsArgs(containerId, config);

      await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
      {
        yield return line;
      }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context,
        StreamEventsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = "events --format \"{{json .}}\"";
      if (config?.Since != null)
        args += $" --since {config.Since}";
      if (config?.Until != null)
        args += $" --until {config.Until}";
      if (config?.Filters != null)
      {
        foreach (var filter in config.Filters)
          args += $" --filter {filter.Key}={filter.Value}";
      }

      await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
      {
        ContainerEvent evt = null;
        try
        {
          evt = JsonConvert.DeserializeObject<ContainerEvent>(line);
          evt?.RawJson = line;
        }
        catch
        {
          // Skip malformed lines
        }

        if (evt != null)
          yield return evt;
      }
    }

    /// <summary>
    /// Builds the CLI arguments string for streaming container stats.
    /// </summary>
    /// <param name="containerId">Container ID or name (null for all containers).</param>
    /// <param name="config">Stream stats configuration.</param>
    /// <returns>The CLI arguments string.</returns>
    public static string BuildStreamStatsArgs(string containerId, StreamStatsConfig config)
    {
      var args = "stats --format \"{{json .}}\"";
      if (config?.Stream == false)
        args += " --no-stream";
      if (config?.All == true)
        args += " -a";
      if (!string.IsNullOrEmpty(containerId))
        args += $" {containerId}";
      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerStats> StreamStatsAsync(
        DriverContext context,
        string containerId = null,
        StreamStatsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamStatsArgs(containerId, config);

      await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
      {
        ContainerStats stats = null;
        try
        {
          stats = ParseStreamStatsLine(line);
        }
        catch
        {
          // Skip malformed lines
        }

        if (stats != null)
          yield return stats;
      }
    }

    /// <summary>
    /// Parses a single JSON line from <c>docker stats --format "{{json .}}"</c> output
    /// into a <see cref="ContainerStats"/>. The CLI format uses keys like CPUPerc,
    /// MemUsage, NetIO, BlockIO, PIDs which do not auto-map to <see cref="ContainerStats"/>
    /// properties, so manual parsing is required.
    /// </summary>
    /// <param name="json">A single JSON line from docker stats CLI output.</param>
    /// <returns>A populated <see cref="ContainerStats"/>, or null if parsing fails.</returns>
    public static ContainerStats ParseStreamStatsLine(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      // Docker stats in streaming mode prefixes lines with ANSI escape codes
      // (e.g. ESC[H for cursor home). Extract the JSON object portion.
      var start = json.IndexOf('{');
      var end = json.LastIndexOf('}');
      if (start < 0 || end < start)
        return null;
      json = json[start..(end + 1)];

      try
      {
        var obj = JObject.Parse(json);

        var cpuPerc = PodmanCliContainerDriver.ParsePercent(
            obj["CPUPerc"]?.Value<string>());
        var memPerc = PodmanCliContainerDriver.ParsePercent(
            obj["MemPerc"]?.Value<string>());
        var (memUsage, memLimit) = PodmanCliContainerDriver.ParseMemoryUsage(
            obj["MemUsage"]?.Value<string>());
        var (netRx, netTx) = PodmanCliContainerDriver.ParseIOPair(
            obj["NetIO"]?.Value<string>());
        var (blockRead, blockWrite) = PodmanCliContainerDriver.ParseIOPair(
            obj["BlockIO"]?.Value<string>());

        int.TryParse(
            obj["PIDs"]?.Value<string>(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var pids);

        return new ContainerStats
        {
          ContainerId = obj["ID"]?.Value<string>()
                          ?? obj["Container"]?.Value<string>(),
          Name = obj["Name"]?.Value<string>(),
          CpuPercentage = cpuPerc,
          MemoryPercentage = memPerc,
          MemoryUsage = memUsage,
          MemoryLimit = memLimit,
          NetworkRx = netRx,
          NetworkTx = netTx,
          BlockRead = blockRead,
          BlockWrite = blockWrite,
          Pids = pids,
          RawJson = json
        };
      }
      catch
      {
        return null;
      }
    }

    /// <inheritdoc />
    public Task<CommandResponse<AttachResult>> AttachAsync(
        DriverContext context,
        string containerId,
        AttachConfig config = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        config ??= new AttachConfig();
        var args = "attach";

        if (!config.SigProxy)
          args += " --sig-proxy=false";
        if (!string.IsNullOrEmpty(config.DetachKeys))
          args += $" --detach-keys {config.DetachKeys}";

        args += $" {containerId}";

        var result = ExecuteAttachProcess(args);
        return Task.FromResult(CommandResponse<AttachResult>.Ok(result));
      }
      catch (Exception ex)
      {
        return Task.FromResult(CommandResponse<AttachResult>.Fail(
            ex.Message, ErrorCodes.Container.AttachFailed));
      }
    }
  }
}

