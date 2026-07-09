using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        args += $" --tail {FormatInvariant(config.Tail.Value)}";
      if (!string.IsNullOrEmpty(config.Since))
        args += $" --since {QuoteArgumentIfNeeded(config.Since)}";
      if (!string.IsNullOrEmpty(config.Until))
        args += $" --until {QuoteArgumentIfNeeded(config.Until)}";
      if (config.Details)
        args += " --details";
      args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";
      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context,
        string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await foreach (var entry in StreamLogEntriesAsync(context, containerId, config, cancellationToken)
          .WithCancellation(cancellationToken).ConfigureAwait(false))
        yield return entry.Source == LogStreamSource.Stderr ? $"[stderr] {entry.Line}" : entry.Line;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LogEntry> StreamLogEntriesAsync(
        DriverContext context,
        string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      config ??= new StreamLogsConfig();
      var args = BuildStreamLogsArgs(containerId, config);
      await foreach (var entry in ExecuteStreamingCommandWithSourcesAsync(
          context, args, config.Stdout, config.Stderr, cancellationToken).ConfigureAwait(false))
        yield return entry;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context,
        StreamEventsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = "events --format \"{{json .}}\"";
      if (config?.Since != null)
        args += $" --since {QuoteArgumentIfNeeded(config.Since)}";
      if (config?.Until != null)
        args += $" --until {QuoteArgumentIfNeeded(config.Until)}";
      if (config?.Filters != null)
      {
        foreach (var filter in config.Filters)
          args += $" --filter {QuoteArgumentIfNeeded($"{filter.Key}={filter.Value}")}";
      }

      await foreach (var line in ExecuteStreamingCommandAsync(context, args, cancellationToken).ConfigureAwait(false))
      {
        var evt = ParseEventLine(line, Logger);

        if (evt != null)
          yield return evt;
      }
    }

    /// <summary>
    /// Parses one JSON line from <c>docker events --format "{{json .}}"</c>.
    /// </summary>
    /// <param name="line">The JSON line to parse.</param>
    /// <returns>The parsed event, or null if parsing fails.</returns>
    public static ContainerEvent ParseEventLine(string line)
    {
      return ParseEventLine(line, NullLogger.Instance);
    }

    private static ContainerEvent ParseEventLine(string line, ILogger logger)
    {
      try
      {
        var evt = JsonSerializer.Deserialize<ContainerEvent>(line, JsonHelper.CaseInsensitiveOptions);
        if (evt == null)
          return null;

        evt.RawJson = line;
        var json = JsonHelper.ParseElement(line);
        evt.Action ??= json.GetStringOrDefault("status");
        evt.ActorId ??= json.GetStringOrDefault("id");

        var actor = json.Prop("Actor");
        if (actor?.ValueKind == JsonValueKind.Object)
        {
          evt.ActorId = actor.Value.GetStringOrDefault("ID") ?? evt.ActorId;
          evt.ActorAttributes = actor.Value.GetStringDictionary("Attributes");
        }

        var time = json.Prop("time");
        if (time?.ValueKind == JsonValueKind.Number && time.Value.TryGetInt64(out var seconds))
          evt.Timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        var timeNano = json.Prop("timeNano");
        if (timeNano?.ValueKind == JsonValueKind.Number && timeNano.Value.TryGetInt64(out var nanos))
          evt.TimeNano = nanos;

        return evt;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Event stream JSON parsing failed");
        return null;
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
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";
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

      await foreach (var line in ExecuteStreamingCommandAsync(context, args, cancellationToken).ConfigureAwait(false))
      {
        ContainerStats stats = null;
        try
        {
          stats = ParseStreamStatsLine(line, Logger);
        }
        catch (Exception ex)
        {
          Logger.LogWarning(ex, "Stats stream JSON parsing failed");
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
      return ParseStreamStatsLine(json, NullLogger.Instance);
    }

    private static ContainerStats ParseStreamStatsLine(string json, ILogger logger)
    {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      // Docker stats in streaming mode prefixes lines with ANSI escape codes
      // (e.g. ESC[H for cursor home). Extract the JSON object portion.
      var start = json.IndexOf('{');
      var end = json.LastIndexOf('}');
      if (start < 0 || end < start)
      {
        logger.LogDebug("Stats line skipped: no JSON object (ANSI control frame)");
        return null;
      }
      json = json[start..(end + 1)];

      try
      {
        var obj = JsonHelper.ParseElement(json);

        var cpuPerc = CliOutputParser.ParsePercent(
            obj.GetStringOrDefault("CPUPerc"));
        var memPerc = CliOutputParser.ParsePercent(
            obj.GetStringOrDefault("MemPerc"));
        var (memUsage, memLimit) = CliOutputParser.ParseMemoryUsage(
            obj.GetStringOrDefault("MemUsage"));
        var (netRx, netTx) = CliOutputParser.ParseIOPair(
            obj.GetStringOrDefault("NetIO"));
        var (blockRead, blockWrite) = CliOutputParser.ParseIOPair(
            obj.GetStringOrDefault("BlockIO"));

        int.TryParse(
            obj.GetStringOrDefault("PIDs"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var pids);

        return new ContainerStats
        {
          ContainerId = obj.GetStringOrDefault("ID")
                          ?? obj.GetStringOrDefault("Container"),
          Name = obj.GetStringOrDefault("Name"),
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
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Stats line parsing failed");
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
        if (config.Stdin == false)
          args += " --no-stdin";
        if (!string.IsNullOrEmpty(config.DetachKeys))
          args += $" --detach-keys {QuoteArgumentIfNeeded(config.DetachKeys)}";
        if (config.Tty || !config.Stdout || !config.Stderr || config.NoStdout || config.NoStderr)
          return Task.FromResult(CommandResponse<AttachResult>.Fail(
              "Docker CLI attach cannot change TTY/stdout/stderr streams; create the container with those settings instead.",
              ErrorCodes.General.InvalidArgument));

        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = ExecuteAttachProcess(context, args, cancellationToken);
        return Task.FromResult(CommandResponse<AttachResult>.Ok(result));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return Task.FromResult(CommandResponse<AttachResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Container.AttachFailed)));
      }
    }
  }
}
