#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
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
    // ponytail: 4 MiB is enough for one pretty stats JSON batch; expose a knob if real streams exceed it.
    private const int MaxStatsJsonBufferChars = 4 * 1024 * 1024;
    private int _detailsWarningLogged;

    /// <summary>Creates a new instance with the specified binary resolver.</summary>
    public PodmanCliStreamDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <summary>
    /// Builds the CLI arguments string for streaming container logs.
    /// </summary>
    /// <param name="containerId">Container ID or name.</param>
    /// <param name="config">Stream logs configuration (null uses defaults).</param>
    /// <returns>The CLI arguments string.</returns>
    /// <remarks>Podman CLI has no Docker-equivalent <c>--details</c>; <see cref="StreamLogsConfig.Details"/> is ignored.</remarks>
    public static string BuildStreamLogsArgs(string containerId, StreamLogsConfig config)
    {
      config ??= new StreamLogsConfig();
      var args = "logs";
      if (config.Follow)
        args += " --follow";
      if (config.Timestamps)
        args += " --timestamps";
      if (config.Tail.HasValue)
        args += $" --tail {config.Tail.Value.ToString(CultureInfo.InvariantCulture)}";
      if (!string.IsNullOrEmpty(config.Since))
        args += $" --since {QuoteArgumentIfNeeded(config.Since)}";
      if (!string.IsNullOrEmpty(config.Until))
        args += $" --until {QuoteArgumentIfNeeded(config.Until)}";
      // ponytail: podman logs has no `--details` flag (unlike docker logs); Details is a no-op here.
      args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";
      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context, string containerId,
        StreamLogsConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await foreach (var entry in StreamLogEntriesAsync(context, containerId, config, cancellationToken)
          .WithCancellation(cancellationToken).ConfigureAwait(false))
        yield return entry.Source == LogStreamSource.Stderr ? $"[stderr] {entry.Line}" : entry.Line;
    }

    /// <inheritdoc />
    /// <remarks>Podman CLI has no Docker-equivalent <c>--details</c>; <see cref="StreamLogsConfig.Details"/> is ignored and warned once per driver instance.</remarks>
    public async IAsyncEnumerable<LogEntry> StreamLogEntriesAsync(
        DriverContext context, string containerId,
        StreamLogsConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      config ??= new StreamLogsConfig();
      if (config.Details && Interlocked.Exchange(ref _detailsWarningLogged, 1) == 0)
        Logger.LogWarning("Podman CLI ignores StreamLogsConfig.Details because podman logs has no --details flag.");
      var args = BuildStreamLogsArgs(containerId, config);

      await foreach (var entry in ExecuteStreamingCommandWithSourcesAsync(
          context, args, config.Stdout, config.Stderr, cancellationToken).ConfigureAwait(false))
        yield return entry;
    }

    /// <summary>
    /// Builds the CLI arguments string for streaming events.
    /// </summary>
    public static string BuildStreamEventsArgs(StreamEventsConfig config)
    {
      var args = "events --format json";
      if (!string.IsNullOrEmpty(config?.Since))
        args += $" --since {QuoteArgumentIfNeeded(config.Since)}";
      if (!string.IsNullOrEmpty(config?.Until))
        args += $" --until {QuoteArgumentIfNeeded(config.Until)}";

      if (config?.Types != null)
        foreach (var type in config.Types)
          args += $" --filter {QuoteArgumentIfNeeded($"type={type}")}";

      if (config?.Actions != null)
        foreach (var action in config.Actions)
          args += $" --filter {QuoteArgumentIfNeeded($"event={action}")}";

      if (config?.Filters != null)
        foreach (var filter in config.Filters)
          args += $" --filter {QuoteArgumentIfNeeded($"{filter.Key}={filter.Value}")}";

      return args;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context, StreamEventsConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamEventsArgs(config);

      await foreach (var line in ExecuteStreamingCommandAsync(context, args, cancellationToken).ConfigureAwait(false))
      {
        var evt = ParseEventCore(line, Logger);
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
      var args = "stats --no-reset --format json";
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
        DriverContext context, string? containerId = null,
        StreamStatsConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = BuildStreamStatsArgs(containerId, config);
      var buffer = new StringBuilder();
      var depth = 0;
      var started = false;
      var inString = false;
      var escaped = false;

      await foreach (var line in ExecuteStreamingCommandAsync(context, args, cancellationToken).ConfigureAwait(false))
      {
        // A line the bounded reader truncated is unparseable JSON (an unbalanced bracket would
        // wedge the depth counter); discard the accumulator and resync on the next batch.
        if (line.EndsWith(BoundedLineReader.TruncationMarker, StringComparison.Ordinal))
        {
          Logger.LogWarning("Podman stats line exceeded the streaming line cap; resetting parser state.");
          buffer.Clear();
          started = false;
          depth = 0;
          inString = false;
          escaped = false;
          continue;
        }

        UpdateJsonState(line, ref started, ref depth, ref inString, ref escaped);
        if (!started)
          continue;

        if (buffer.Length > 0)
          buffer.Append('\n');
        buffer.Append(line);
        if (buffer.Length > MaxStatsJsonBufferChars)
        {
          Logger.LogWarning("Podman stats JSON buffer exceeded {Limit} chars; resetting parser state.", MaxStatsJsonBufferChars);
          buffer.Clear();
          started = false;
          depth = 0;
          inString = false;
          escaped = false;
          continue;
        }

        if (depth != 0)
          continue;

        foreach (var stats in ParseStatsBatch(buffer.ToString(), Logger))
          yield return stats;

        buffer.Clear();
        started = false;
        inString = false;
        escaped = false;
      }
    }

    /// <inheritdoc />
    public Task<CommandResponse<AttachResult>> AttachAsync(
        DriverContext context, string containerId,
        AttachConfig? config = null,
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
              "Podman CLI attach cannot change TTY/stdout/stderr streams; create the container with those settings instead.",
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

    #region Parsing

    private static ContainerEvent ParseEventCore(string json, ILogger logger)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(json))
          return null;

        var obj = JsonHelper.ParseElement(json);
        var actorProp = obj.Prop("Actor");
        string actorId = null;
        Dictionary<string, string> attributes = [];
        if (actorProp.HasValue)
        {
          actorId = actorProp.Value.GetStringOrDefault("ID");
          attributes = actorProp.Value.GetStringDictionary("Attributes");
        }

        var topAttributes = obj.GetStringDictionary("Attributes");
        if (topAttributes.Count > 0)
          attributes = topAttributes;

        var evt = new ContainerEvent
        {
          Type = obj.GetStringOrDefault("Type") ?? obj.GetStringOrDefault("type"),
          Action = obj.GetStringOrDefault("Action")
                   ?? obj.GetStringOrDefault("Status")
                   ?? obj.GetStringOrDefault("status"),
          ActorId = actorId
                    ?? obj.GetStringOrDefault("ID")
                    ?? obj.GetStringOrDefault("id"),
          ActorAttributes = attributes,
          RawJson = json
        };

        ApplyEventTime(obj, evt);
        return evt;
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "Podman event parsing failed");
        return null;
      }
    }

    private static void ApplyEventTime(JsonElement obj, ContainerEvent evt)
    {
      var time = obj.Prop("time") ?? obj.Prop("Time");
      if (time?.ValueKind == JsonValueKind.Number && time.Value.TryGetInt64(out var seconds))
        evt.Timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
      else if (time?.ValueKind == JsonValueKind.String
               && DateTimeOffset.TryParse(
                   time.Value.GetString(),
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var timestamp))
        evt.Timestamp = timestamp.UtcDateTime;

      var timeNano = obj.Prop("timeNano") ?? obj.Prop("TimeNano");
      if (timeNano?.ValueKind == JsonValueKind.Number && timeNano.Value.TryGetInt64(out var nanos))
        evt.TimeNano = nanos;
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
      foreach (var stats in ParseStatsBatch(json, NullLogger.Instance))
        return stats;

      return null;
    }

    private static List<ContainerStats> ParseStatsBatch(string json, ILogger logger)
    {
      var stats = new List<ContainerStats>();
      if (string.IsNullOrWhiteSpace(json))
        return stats;

      Exception lastError = null;
      foreach (var candidate in JsonCandidates(json))
      {
        try
        {
          var root = JsonHelper.ParseElement(candidate);
          if (root.ValueKind == JsonValueKind.Array)
          {
            foreach (var item in root.EnumerateArraySafe())
              if (item.ValueKind == JsonValueKind.Object)
                stats.Add(MapStats(item));
          }
          else if (root.ValueKind == JsonValueKind.Object)
          {
            stats.Add(MapStats(root));
          }

          return stats;
        }
        catch (Exception ex)
        {
          lastError = ex;
        }
      }

      if (lastError != null)
        logger.LogDebug(lastError, "Podman stats parsing failed");
      return stats;
    }

    private static ContainerStats MapStats(JsonElement token)
    {
      var raw = token.GetRawText();
      var result = PodmanCliContainerDriver.ParseStatsOutput(raw);
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
        Timestamp = DateTime.UtcNow,
        RawJson = raw
      };
    }

    private static IEnumerable<string> JsonCandidates(string text)
    {
      var trimmed = text.Trim();
      for (var i = 0; i < trimmed.Length; i++)
      {
        var ch = trimmed[i];
        if (ch != '{' && ch != '[')
          continue;

        var end = ch == '[' ? trimmed.LastIndexOf(']') : trimmed.LastIndexOf('}');
        if (end > i)
          yield return trimmed[i..(end + 1)];
      }
    }

    private static void UpdateJsonState(
        string line, ref bool started, ref int depth, ref bool inString, ref bool escaped)
    {
      var previous = '\0';
      foreach (var ch in line)
      {
        if (!started)
        {
          if (ch != '{' && (ch != '[' || previous == '\u001b'))
          {
            previous = ch;
            continue;
          }
          started = true;
          depth = 1;
          previous = ch;
          continue;
        }

        if (escaped)
        {
          escaped = false;
          previous = ch;
          continue;
        }

        if (ch == '\\' && inString)
        {
          escaped = true;
          previous = ch;
          continue;
        }

        if (ch == '"')
        {
          inString = !inString;
          previous = ch;
          continue;
        }

        if (inString)
        {
          previous = ch;
          continue;
        }

        if (ch == '{' || ch == '[')
          depth++;
        else if (ch == '}' || ch == ']')
          depth--;

        if (depth == 0)
          break;

        previous = ch;
      }
    }

    #endregion
  }
}
