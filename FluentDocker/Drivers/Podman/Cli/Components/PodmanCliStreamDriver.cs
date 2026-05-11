using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of IStreamDriver.
  /// </summary>
  public class PodmanCliStreamDriver : PodmanCliDriverBase, IStreamDriver
  {
    public PodmanCliStreamDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
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
        args += " --follow";
      if (config.Timestamps)
        args += " --timestamps";
      if (config.Tail.HasValue)
        args += $" --tail {config.Tail.Value}";
      if (!string.IsNullOrEmpty(config.Since))
        args += $" --since {config.Since}";
      if (!string.IsNullOrEmpty(config.Until))
        args += $" --until {config.Until}";
      args += $" {containerId}";
      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context, string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamLogsArgs(containerId, config);

      await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
      {
        yield return line;
      }
    }

    /// <summary>
    /// Builds the CLI arguments string for streaming events.
    /// </summary>
    public static string BuildStreamEventsArgs(StreamEventsConfig config)
    {
      var args = "events --format json";
      if (!string.IsNullOrEmpty(config?.Since))
        args += $" --since {config.Since}";
      if (!string.IsNullOrEmpty(config?.Until))
        args += $" --until {config.Until}";

      if (config?.Types != null)
        foreach (var type in config.Types)
          args += $" --filter type={type}";

      if (config?.Actions != null)
        foreach (var action in config.Actions)
          args += $" --filter event={action}";

      if (config?.Filters != null)
        foreach (var filter in config.Filters)
          args += $" --filter {filter.Key}={filter.Value}";

      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context, StreamEventsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamEventsArgs(config);

      await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
      {
        var evt = ParseEvent(line);
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
      var args = "stats --format json";
      if (config?.Stream == false)
        args += " --no-stream";
      if (config?.NoHeader == true)
        args += " --no-header";
      if (config?.All == true)
        args += " -a";
      if (!string.IsNullOrEmpty(containerId))
        args += $" {containerId}";
      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerStats> StreamStatsAsync(
        DriverContext context, string containerId = null,
        StreamStatsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamStatsArgs(containerId, config);

      await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
      {
        var stats = ParseStats(line);
        if (stats != null)
          yield return stats;
      }
    }

    /// <inheritdoc />
    public Task<CommandResponse<AttachResult>> AttachAsync(
        DriverContext context, string containerId,
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

    #region Parsing

    private static ContainerEvent ParseEvent(string json)
    {
      try
      {
        var obj = JsonHelper.ParseElement(json);
        var actorProp = obj.Prop("Actor");
        string actorId = null;
        if (actorProp.HasValue)
          actorId = actorProp.Value.GetStringOrDefault("ID");

        return new ContainerEvent
        {
          Type = obj.GetStringOrDefault("Type") ?? obj.GetStringOrDefault("type"),
          Action = obj.GetStringOrDefault("Action") ?? obj.GetStringOrDefault("Status"),
          ActorId = actorId ?? obj.GetStringOrDefault("id"),
          RawJson = json
        };
      }
      catch (Exception ex)
      {
        NullLogger.Instance.LogDebug(ex, "Podman event parsing failed");
        return null;
      }
    }

    /// <summary>
    /// Parses a single JSON line from <c>podman stats --format json</c> output
    /// into a <see cref="ContainerStats"/>. Delegates to
    /// <see cref="PodmanCliContainerDriver.ParseStatsOutput"/> for the heavy lifting,
    /// then maps the <see cref="ContainerStatsResult"/> to <see cref="ContainerStats"/>.
    /// </summary>
    /// <param name="json">A single JSON line from podman stats output.</param>
    /// <returns>A populated <see cref="ContainerStats"/>, or null if parsing fails.</returns>
    public static ContainerStats ParseStats(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      // Podman stats in streaming mode may prefix lines with ANSI escape codes.
      // Extract the JSON object portion.
      var start = json.IndexOf('{');
      var end = json.LastIndexOf('}');
      if (start < 0 || end < start)
        return null;
      json = json[start..(end + 1)];

      try
      {
        var result = PodmanCliContainerDriver.ParseStatsOutput(json);

        return new ContainerStats
        {
          ContainerId = result.ContainerId,
          Name = result.Name,
          CpuPercentage = result.CpuPercent,
          MemoryUsage = result.MemoryUsage,
          MemoryLimit = result.MemoryLimit,
          MemoryPercentage = result.MemoryPercent,
          NetworkRx = result.NetworkRxBytes,
          NetworkTx = result.NetworkTxBytes,
          BlockRead = result.BlockReadBytes,
          BlockWrite = result.BlockWriteBytes,
          Pids = result.Pids,
          RawJson = json
        };
      }
      catch (Exception ex)
      {
        NullLogger.Instance.LogDebug(ex, "Podman stats parsing failed");
        return null;
      }
    }

    #endregion
  }
}
