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
using Microsoft.Extensions.Logging.Abstractions;

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
      config ??= new StreamLogsConfig();
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/logs?" +
          $"follow={config.Follow.ToString().ToLower()}" +
          $"&stdout={config.Stdout.ToString().ToLower()}" +
          $"&stderr={config.Stderr.ToString().ToLower()}" +
          $"&timestamps={config.Timestamps.ToString().ToLower()}";

      if (config.Tail.HasValue)
        path += $"&tail={config.Tail.Value}";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={Uri.EscapeDataString(config.Since)}";
      if (!string.IsNullOrEmpty(config.Until))
        path += $"&until={Uri.EscapeDataString(config.Until)}";

      Stream stream;
      try
      {
        stream = await Connection.GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Docker log stream open failed");
        yield break;
      }

      // Docker log streams use multiplexed format with 8-byte headers
      // unless the container was started with TTY mode.
      // Use try/finally to dispose the stream when the caller breaks out.
      try
      {
        await foreach (var line in ReadMultiplexedStreamAsync(stream, cancellationToken))
        {
          yield return line;
        }
      }
      finally
      {
        await stream.DisposeAsync().ConfigureAwait(false);
      }
    }

    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context, StreamEventsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      config ??= new StreamEventsConfig();

      var filters = new Dictionary<string, List<string>>();
      if (config.Types?.Count > 0)
        filters["type"] = config.Types;
      if (config.Actions?.Count > 0)
        filters["event"] = config.Actions;

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
      if (string.IsNullOrEmpty(containerId))
        yield break;

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

    public async Task<CommandResponse<AttachResult>> AttachAsync(
        DriverContext context, string containerId,
        AttachConfig config = null, CancellationToken cancellationToken = default)
    {
      config ??= new AttachConfig();
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/attach?" +
          $"stream=1" +
          $"&stdout={config.Stdout.ToString().ToLower()}" +
          $"&stderr={config.Stderr.ToString().ToLower()}" +
          $"&stdin={config.Stdin.ToString().ToLower()}";

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
    /// Reads Docker multiplexed stream format.
    /// Header: [stream_type:1][0:3][size:4 big-endian] followed by payload.
    /// stream_type: 0=stdin, 1=stdout, 2=stderr.
    /// </summary>
    private static async IAsyncEnumerable<string> ReadMultiplexedStreamAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
      var header = new byte[8];

      while (!ct.IsCancellationRequested)
      {
        int bytesRead;
        try
        {
          bytesRead = await ReadExactAsync(stream, header, 8, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { yield break; }
        catch (Exception ex) { NullLogger.Instance.LogError(ex, "Multiplexed stream read error"); yield break; }

        if (bytesRead < 8)
        {
          // Possibly a raw (TTY) stream -- try reading as plain text
          if (bytesRead > 0)
          {
            var partial = Encoding.UTF8.GetString(header, 0, bytesRead);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024, leaveOpen: true);
            var rest = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            foreach (var line in (partial + rest).Split('\n'))
            {
              if (!string.IsNullOrEmpty(line))
                yield return line;
            }
          }
          yield break;
        }

        var frameSize = (header[4] << 24) | (header[5] << 16) |
            (header[6] << 8) | header[7];

        if (frameSize <= 0 || frameSize > MaxFrameSizeBytes)
          yield break;

        var payload = new byte[frameSize];
        var payloadRead = await ReadExactAsync(stream, payload, frameSize, ct).ConfigureAwait(false);
        if (payloadRead < frameSize)
          yield break;

        var text = Encoding.UTF8.GetString(payload, 0, payloadRead).TrimEnd('\n', '\r');
        foreach (var line in text.Split('\n'))
        {
          if (!string.IsNullOrEmpty(line))
            yield return line;
        }
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
          var op = entry.GetStringOrDefault("op")?.ToLower();
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
