using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IStreamDriver.
  /// Uses streaming endpoints for logs, events, stats, and attach.
  /// </summary>
  public class DockerApiStreamDriver : DockerApiDriverBase, IStreamDriver
  {
    /// <summary>Maximum allowed frame size in the Docker multiplexed stream protocol (10 MB).</summary>
    private const int MaxFrameSizeBytes = 10 * 1024 * 1024;

    public DockerApiStreamDriver(IDockerApiConnection connection) : base(connection) { }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context, string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await foreach (var entry in StreamEntriesAsync(containerId, config, cancellationToken)
          .ConfigureAwait(false))
      {
        yield return entry.Line;
      }
    }

    /// <summary>
    /// Streams source-tagged log entries. The originating stream (stdout/stderr) is taken
    /// from the Docker multiplexed stream header, which the line-based <see cref="StreamLogsAsync"/>
    /// discards. Each yielded item is frame-granular unless the Docker frame itself contains
    /// newline-separated lines.
    /// </summary>
    public async IAsyncEnumerable<LogEntry> StreamLogEntriesAsync(
        DriverContext context, string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await foreach (var entry in StreamEntriesAsync(containerId, config, cancellationToken)
          .ConfigureAwait(false))
      {
        yield return entry;
      }
    }

    private static string BuildLogsPath(string containerId, StreamLogsConfig config)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/logs?" +
          $"follow={config.Follow.ToString().ToLowerInvariant()}" +
          $"&stdout={config.Stdout.ToString().ToLowerInvariant()}" +
          $"&stderr={config.Stderr.ToString().ToLowerInvariant()}" +
          $"&timestamps={config.Timestamps.ToString().ToLowerInvariant()}";

      if (config.Tail.HasValue)
        path += $"&tail={config.Tail.Value}";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={Uri.EscapeDataString(config.Since)}";
      if (!string.IsNullOrEmpty(config.Until))
        path += $"&until={Uri.EscapeDataString(config.Until)}";
      return path;
    }

    private async IAsyncEnumerable<LogEntry> StreamEntriesAsync(
        string containerId, StreamLogsConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      config ??= new StreamLogsConfig();
      var path = BuildLogsPath(containerId, config);

      // A TTY container's log stream is raw text, not the 8-byte multiplexed frame format.
      // Misreading raw output as multiplexed corrupts/drops lines, so detect TTY up-front.
      var tty = await DetectTtyAsync(containerId, cancellationToken).ConfigureAwait(false);

      Stream stream;
      try
      {
        stream = await Connection.GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Docker log stream open failed");
        throw new DriverException(
            $"Failed to open Docker log stream for container '{containerId}': {ex.Message}",
            ErrorCodes.Api.ServerError, ex);
      }

      // Use try/finally to dispose the stream when the caller breaks out.
      try
      {
        await foreach (var entry in ReadMultiplexedStreamAsync(stream, tty, cancellationToken))
        {
          yield return entry;
        }
      }
      finally
      {
        await stream.DisposeAsync().ConfigureAwait(false);
      }
    }

    /// <summary>
    /// Inspects the container to determine whether it was started with a TTY. A TTY stream
    /// is raw text (no multiplex headers). On any inspect failure we default to demux=false
    /// so a transient inspect error never crashes log streaming.
    /// </summary>
    private async Task<bool> DetectTtyAsync(string containerId, CancellationToken ct)
    {
      try
      {
        var result = await GetJsonElementAsync(
            $"/containers/{Uri.EscapeDataString(containerId)}/json", ct).ConfigureAwait(false);
        if (result.Success && result.Data.ValueKind == JsonValueKind.Object)
        {
          var config = result.Data.Prop("Config");
          if (config?.ValueKind == JsonValueKind.Object)
            return config.Value.GetBoolOrDefault("Tty");
        }
      }
      catch (Exception ex)
      {
        Logger.LogDebug(ex, "Could not determine container TTY mode; defaulting to demux");
      }
      return false;
    }

    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context, StreamEventsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      config ??= new StreamEventsConfig();

      var filters = new Dictionary<string, List<string>>();
      if (config.Types?.Count > 0)
        filters["type"] = new List<string>(config.Types);
      if (config.Actions?.Count > 0)
        filters["event"] = new List<string>(config.Actions);

      // Merge caller-supplied custom filters (e.g. label=foo, container=id) into the
      // Docker filters map, unioned with the type/event entries above. Null-safe to match
      // the Types/Actions guards above, in case a caller nulls out the dictionary.
      if (config.Filters?.Count > 0)
      {
        foreach (var kv in config.Filters)
        {
          if (!filters.TryGetValue(kv.Key, out var values))
          {
            values = new List<string>();
            filters[kv.Key] = values;
          }
          values.Add(kv.Value);
        }
      }

      var queryParams = new List<string>();
      if (filters.Count > 0)
        queryParams.Add($"filters={Uri.EscapeDataString(JsonHelper.Serialize(filters))}");
      if (!string.IsNullOrEmpty(config.Since))
        queryParams.Add($"since={Uri.EscapeDataString(config.Since)}");
      if (!string.IsNullOrEmpty(config.Until))
        queryParams.Add($"until={Uri.EscapeDataString(config.Until)}");

      var path = queryParams.Count > 0
          ? $"/events?{string.Join("&", queryParams)}"
          : "/events";

      await foreach (var line in ReadNdjsonStreamAsync(path, cancellationToken))
      {
        ContainerEvent evt;
        try
        {
          var json = JsonHelper.ParseElement(line);
          evt = new ContainerEvent
          {
            Type = json.GetStringOrDefault("Type"),
            Action = json.GetStringOrDefault("Action"),
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(
                  json.GetInt64OrDefault("time")).UtcDateTime,
            TimeNano = json.GetInt64OrDefault("timeNano"),
            Scope = json.GetStringOrDefault("scope"),
            RawJson = line
          };

          var actor = json.Prop("Actor");
          if (actor?.ValueKind == JsonValueKind.Object)
          {
            evt.ActorId = actor.Value.GetStringOrDefault("ID");
            evt.ActorAttributes = actor.Value.GetStringDictionary("Attributes");
          }
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Event stream JSON parsing failed");
          continue;
        }

        yield return evt;
      }
    }

    public async IAsyncEnumerable<ContainerStats> StreamStatsAsync(
        DriverContext context, string containerId = null,
        StreamStatsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      // Docker Engine API requires a specific container ID for stats;
      // there is no all-container stats endpoint.
      if (string.IsNullOrWhiteSpace(containerId))
        throw new ArgumentException("Container ID is required for Docker API stats streaming.", nameof(containerId));

      config ??= new StreamStatsConfig();
      var stream = config.Stream ? "true" : "false";
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/stats?stream={stream}";

      await foreach (var line in ReadNdjsonStreamAsync(path, cancellationToken))
      {
        ContainerStats stats;
        try
        {
          stats = ParseContainerStats(line);
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Stats stream JSON parsing failed");
          continue;
        }

        if (stats != null)
          yield return stats;
      }
    }

    /// <summary>
    /// Attaches to stdout/stderr through the Docker API attach endpoint.
    /// Interactive stdin is rejected because this driver does not implement HTTP hijacking.
    /// The returned <see cref="AttachResult.OutputStream"/> is the raw Docker attach stream;
    /// when TTY is disabled it may contain Docker multiplexed frames.
    /// </summary>
    public async Task<CommandResponse<AttachResult>> AttachAsync(
        DriverContext context, string containerId,
        AttachConfig config = null, CancellationToken cancellationToken = default)
    {
      config ??= new AttachConfig();
      if (config.Stdin)
        return CommandResponse<AttachResult>.Fail(
            "interactive stdin is not supported by the Docker API driver",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach", 0));

      var path = $"/containers/{Uri.EscapeDataString(containerId)}/attach?" +
          $"stream=1" +
          $"&stdout={config.Stdout.ToString().ToLowerInvariant()}" +
          $"&stderr={config.Stderr.ToString().ToLowerInvariant()}" +
          $"&stdin={config.Stdin.ToString().ToLowerInvariant()}";

      try
      {
        using var content = new StringContent("", Encoding.UTF8);
        var stream = await Connection.PostStreamAsync(path, content, cancellationToken).ConfigureAwait(false);

        return CommandResponse<AttachResult>.Ok(new AttachResult
        {
          OutputStream = stream,
          IsConnected = true
        });
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<AttachResult>.Fail(
            $"Attach failed: {ex.Message}",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach",
                0, ex.Message));
      }
    }

    #region Multiplexed Stream Reader

    /// <summary>
    /// Reads Docker multiplexed stream format, tagging each line with its source stream.
    /// Header: [stream_type:1][0:3][size:4 big-endian] followed by payload.
    /// stream_type: 0=stdin, 1=stdout, 2=stderr.
    /// When <paramref name="tty"/> is true the stream is raw text (no headers), so
    /// demultiplexing is bypassed and every line is tagged as stdout.
    /// </summary>
    private static async IAsyncEnumerable<LogEntry> ReadMultiplexedStreamAsync(
        Stream stream, bool tty, [EnumeratorCancellation] CancellationToken ct)
    {
      if (tty)
      {
        await foreach (var entry in ReadRawTextStreamAsync(stream, ct))
          yield return entry;
        yield break;
      }

      var header = new byte[8];

      while (!ct.IsCancellationRequested)
      {
        int bytesRead;
        try
        {
          bytesRead = await ReadExactAsync(stream, header, 8, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { yield break; }
        catch (Exception ex)
        {
          throw new DriverException(
              $"Docker log stream read failed: {ex.Message}", ErrorCodes.Api.ServerError, ex);
        }

        if (bytesRead == 0)
          yield break;
        if (bytesRead < 8)
        {
          throw new DriverException(
              $"Docker log stream truncated: partial {bytesRead}-byte frame header",
              ErrorCodes.Api.ServerError);
        }

        if (header[0] > 2 || header[1] != 0 || header[2] != 0 || header[3] != 0)
        {
          throw new DriverException(
              "Docker log stream has an invalid multiplexed frame header",
              ErrorCodes.Api.ServerError);
        }

        var source = MapSource(header[0]);

        var frameSize = (header[4] << 24) | (header[5] << 16) |
            (header[6] << 8) | header[7];

        if (frameSize < 0 || frameSize > MaxFrameSizeBytes)
          throw new DriverException(
              $"Docker log stream frame size {frameSize} is invalid or exceeds the {MaxFrameSizeBytes} byte limit",
              ErrorCodes.Api.ServerError);
        if (frameSize == 0)
          continue;

        var payload = new byte[frameSize];
        var payloadRead = await ReadExactAsync(stream, payload, frameSize, ct).ConfigureAwait(false);
        if (payloadRead < frameSize)
          throw new DriverException(
              $"Docker log stream truncated: expected {frameSize} payload bytes, read {payloadRead}",
              ErrorCodes.Api.ServerError);

        var text = Encoding.UTF8.GetString(payload, 0, payloadRead).TrimEnd('\n', '\r');
        foreach (var line in text.Split('\n'))
        {
          if (!string.IsNullOrEmpty(line))
            yield return new LogEntry { Source = source, Line = line };
        }
      }
    }

    private static LogStreamSource MapSource(byte streamType) => streamType switch
    {
      0 => LogStreamSource.Stdin,
      2 => LogStreamSource.Stderr,
      _ => LogStreamSource.Stdout,
    };

    /// <summary>
    /// Reads a raw (TTY) log stream as plain UTF-8 text, yielding each non-empty line as
    /// stdout. Raw streams carry no source byte, so stderr cannot be distinguished.
    /// </summary>
    private static async IAsyncEnumerable<LogEntry> ReadRawTextStreamAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
      using var reader = new StreamReader(stream, Encoding.UTF8,
          detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
      while (!ct.IsCancellationRequested)
      {
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { yield break; }

        if (line == null)
          break;
        if (line.Length == 0)
          continue;
        yield return new LogEntry { Source = LogStreamSource.Stdout, Line = line };
      }
    }

    // ReadExactAsync is inherited from DockerApiDriverBase

    #endregion

    #region Stats Parsing

    private static ContainerStats ParseContainerStats(string json)
    {
      var obj = JsonHelper.ParseElement(json);
      var stats = new ContainerStats
      {
        ContainerId = obj.GetStringOrDefault("id"),
        Name = obj.GetStringOrDefault("name")?.TrimStart('/'),
        RawJson = json,
        Timestamp = obj.GetDateTimeOrDefault("read")
      };
      if (stats.Timestamp == DateTime.MinValue)
        stats.Timestamp = DateTime.UtcNow;

      var cpuStats = obj.Prop("cpu_stats");
      var preCpuStats = obj.Prop("precpu_stats");
      if (cpuStats?.ValueKind == JsonValueKind.Object &&
          preCpuStats?.ValueKind == JsonValueKind.Object)
      {
        var cpuDelta =
            (cpuStats.Value.Prop("cpu_usage")?.GetInt64OrDefault("total_usage") ?? 0) -
            (preCpuStats.Value.Prop("cpu_usage")?.GetInt64OrDefault("total_usage") ?? 0);
        var systemDelta =
            cpuStats.Value.GetInt64OrDefault("system_cpu_usage") -
            preCpuStats.Value.GetInt64OrDefault("system_cpu_usage");
        var numCpus = cpuStats.Value.GetInt32OrDefault("online_cpus", 1);

        if (systemDelta > 0 && cpuDelta > 0)
          stats.CpuPercentage = (double)cpuDelta / systemDelta * numCpus * 100.0;
      }

      // Memory
      var memStats = obj.Prop("memory_stats");
      if (memStats?.ValueKind == JsonValueKind.Object)
      {
        stats.MemoryUsage = memStats.Value.GetInt64OrDefault("usage");
        stats.MemoryLimit = memStats.Value.GetInt64OrDefault("limit");
        if (stats.MemoryLimit > 0)
          stats.MemoryPercentage = (double)stats.MemoryUsage / stats.MemoryLimit * 100.0;
      }

      // Network
      var networks = obj.Prop("networks");
      if (networks?.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in networks.Value.EnumerateObject())
        {
          stats.NetworkRx += prop.Value.GetInt64OrDefault("rx_bytes");
          stats.NetworkTx += prop.Value.GetInt64OrDefault("tx_bytes");
        }
      }

      // Block IO
      var blkioStats = obj.Prop("blkio_stats");
      var blkio = blkioStats?.Prop("io_service_bytes_recursive");
      if (blkio?.ValueKind == JsonValueKind.Array)
      {
        foreach (var entry in blkio.Value.EnumerateArray())
        {
          var op = entry.GetStringOrDefault("op")?.ToLowerInvariant();
          var value = entry.GetInt64OrDefault("value");
          if (op == "read")
            stats.BlockRead += value;
          else if (op == "write")
            stats.BlockWrite += value;
        }
      }

      var pidsStats = obj.Prop("pids_stats");
      stats.Pids = pidsStats?.GetInt32OrDefault("current") ?? 0;
      return stats;
    }

    #endregion
  }
}
