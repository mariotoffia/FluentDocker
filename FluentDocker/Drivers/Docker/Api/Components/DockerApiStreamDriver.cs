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
using FluentDocker.Drivers.Connection;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IStreamDriver.
  /// Uses streaming endpoints for logs, events, stats, and attach.
  /// </summary>
  public partial class DockerApiStreamDriver : DockerApiDriverBase, IStreamDriver
  {
    /// <summary>Initializes a Docker API stream driver.</summary>
    public DockerApiStreamDriver(IDockerApiConnection connection) : base(connection) { }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context, string containerId,
        StreamLogsConfig? config = null,
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
        StreamLogsConfig? config = null,
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
            ClassifyStreamException(ex), ex);
      }

      // Use try/finally to dispose the stream when the caller breaks out.
      try
      {
        var contentType = (stream as ResponseOwningStream)?.ContentType;
        if (UseLogContentType(contentType))
        {
          var multiplexed = string.Equals(contentType,
              MultiplexedStreamContentType, StringComparison.OrdinalIgnoreCase);
          await foreach (var entry in ReadMultiplexedStreamAsync(
              stream, !multiplexed, sniffOnInvalidHeader: false, cancellationToken).ConfigureAwait(false))
          {
            yield return entry;
          }
          yield break;
        }

        // Older daemons do not emit the authoritative stream Content-Type; inspect TTY
        // and retain the byte-sniff fallback for that compatibility window.
        var tty = await DetectTtyAsync(containerId, cancellationToken).ConfigureAwait(false);
        var sniffOnInvalidHeader = !tty.HasValue;
        await foreach (var entry in ReadMultiplexedStreamAsync(
            stream, tty == true, sniffOnInvalidHeader, cancellationToken).ConfigureAwait(false))
        {
          yield return entry;
        }
      }
      finally
      {
        await stream.DisposeAsync().ConfigureAwait(false);
      }
    }

    private const string MultiplexedStreamContentType = "application/vnd.docker.multiplexed-stream";
    private const string RawStreamContentType = "application/vnd.docker.raw-stream";

    // Only trust the stream Content-Type when it is one of Docker's authoritative values
    // (API 1.42+). An unrecognized type (e.g. rewritten by a proxy) falls through to the
    // TTY-inspect + byte-sniff path instead of being blindly read as raw.
    private bool UseLogContentType(string contentType)
    {
      return (string.Equals(contentType, MultiplexedStreamContentType, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(contentType, RawStreamContentType, StringComparison.OrdinalIgnoreCase)) &&
          Version.TryParse(Connection.ApiVersion, out var version) &&
          version.CompareTo(new Version(1, 42)) >= 0;
    }

    // DetectTtyAsync is inherited from DockerApiDriverBase (shared with the log-tail reader).

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context, StreamEventsConfig? config = null,
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

      // Mirror StreamStatsCoreAsync: await foreach configures ConfigureAwait(false) on both
      // MoveNextAsync and the enumerator's DisposeAsync. ReadNdjsonStreamAsync already wraps
      // any mid-stream transport read failure as DriverException (StreamInterrupted) and lets
      // caller cancellation surface as OperationCanceledException, so no per-item catch is
      // needed here.
      await foreach (var line in ReadNdjsonStreamAsync(path, cancellationToken)
          .ConfigureAwait(false))
      {
        ContainerEvent evt;
        try
        {
          var json = JsonHelper.ParseElement(line);
          var timestamp = DateTimeOffset.UtcNow.UtcDateTime;
          if (json.TryGetProperty("time", out var time) &&
              time.ValueKind == JsonValueKind.Number && time.TryGetInt64(out var unixSeconds))
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
          evt = new ContainerEvent
          {
            Type = json.GetStringOrDefault("Type"),
            Action = json.GetStringOrDefault("Action"),
            Timestamp = timestamp,
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

      if (string.IsNullOrEmpty(config.Until))
        throw new DriverException(
            "Docker /events stream ended without an 'until' bound (daemon closed the connection)",
            ErrorCodes.Api.StreamEnded);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ContainerStats> StreamStatsAsync(
        DriverContext context, string? containerId = null,
        StreamStatsConfig? config = null,
        CancellationToken cancellationToken = default)
    {
      // Docker Engine API requires a specific container ID for stats;
      // there is no all-container stats endpoint.
      if (string.IsNullOrWhiteSpace(containerId))
        throw new ArgumentException("Container ID is required for Docker API stats streaming.", nameof(containerId));

      return StreamStatsCoreAsync(containerId, config, cancellationToken);
    }

    private async IAsyncEnumerable<ContainerStats> StreamStatsCoreAsync(
        string containerId, StreamStatsConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      config ??= new StreamStatsConfig();
      var stream = config.Stream ? "true" : "false";
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/stats?stream={stream}";

      await foreach (var line in ReadNdjsonStreamAsync(path, cancellationToken).ConfigureAwait(false))
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
        AttachConfig? config = null, CancellationToken cancellationToken = default)
    {
      config ??= new AttachConfig();
      if (config.Stdin == true)
        return CommandResponse<AttachResult>.Fail(
            "interactive stdin is not supported by the Docker API driver",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach", 0));
      if (config.NoStdout)
        return CommandResponse<AttachResult>.Fail(
            "NoStdout is not supported by the Docker API driver",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach", 0));
      if (config.NoStderr)
        return CommandResponse<AttachResult>.Fail(
            "NoStderr is not supported by the Docker API driver",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach", 0));
      if (!string.IsNullOrEmpty(config.DetachKeys))
        return CommandResponse<AttachResult>.Fail(
            "DetachKeys is not supported by the Docker API driver",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach", 0));

      var path = $"/containers/{Uri.EscapeDataString(containerId)}/attach?" +
          $"stream=1" +
          $"&stdout={config.Stdout.ToString().ToLowerInvariant()}" +
          $"&stderr={config.Stderr.ToString().ToLowerInvariant()}" +
          $"&stdin=false";

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
        var statusCode = ex is HttpRequestException { StatusCode: not null } httpEx
            ? (int)httpEx.StatusCode.Value
            : 0;
        return CommandResponse<AttachResult>.Fail(
            $"Attach failed: {ex.Message}",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"POST /containers/{containerId}/attach",
                statusCode, ex.Message),
            statusCode);
      }
    }

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
        var numCpus = cpuStats.Value.GetInt32OrDefault("online_cpus");
        if (numCpus <= 0)
        {
          var perCpu = cpuStats.Value.Prop("cpu_usage")?.Prop("percpu_usage");
          numCpus = perCpu?.ValueKind == JsonValueKind.Array ? perCpu.Value.GetArrayLength() : 1;
        }

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
