using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IStreamDriver.
  /// Uses streaming endpoints for logs, events, stats, and attach.
  /// </summary>
  public class DockerApiStreamDriver : DockerApiDriverBase, IStreamDriver
  {
    public DockerApiStreamDriver(IDockerApiConnection connection) : base(connection) { }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context, string containerId,
        StreamLogsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      config ??= new StreamLogsConfig();
      var path = $"/containers/{containerId}/logs?" +
          $"follow={config.Follow.ToString().ToLower()}" +
          $"&stdout={config.Stdout.ToString().ToLower()}" +
          $"&stderr={config.Stderr.ToString().ToLower()}" +
          $"&timestamps={config.Timestamps.ToString().ToLower()}";

      if (config.Tail.HasValue)
        path += $"&tail={config.Tail.Value}";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={config.Since}";
      if (!string.IsNullOrEmpty(config.Until))
        path += $"&until={config.Until}";

      Stream stream;
      try
      {
        stream = await Connection.GetStreamAsync(path, cancellationToken);
      }
      catch
      {
        yield break;
      }

      // Docker log streams use multiplexed format with 8-byte headers
      // unless the container was started with TTY mode
      await foreach (var line in ReadMultiplexedStreamAsync(stream, cancellationToken))
      {
        yield return line;
      }
    }

    public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context, StreamEventsConfig config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      config ??= new StreamEventsConfig();
      var path = "/events?";

      var filters = new Dictionary<string, List<string>>();
      if (config.Types?.Count > 0)
        filters["type"] = config.Types;
      if (config.Actions?.Count > 0)
        filters["event"] = config.Actions;

      if (filters.Count > 0)
        path += $"filters={Uri.EscapeDataString(JsonConvert.SerializeObject(filters))}";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={config.Since}";
      if (!string.IsNullOrEmpty(config.Until))
        path += $"&until={config.Until}";

      await foreach (var line in ReadNdjsonStreamAsync(path, cancellationToken))
      {
        ContainerEvent evt;
        try
        {
          var json = JObject.Parse(line);
          evt = new ContainerEvent
          {
            Type = json.Value<string>("Type"),
            Action = json.Value<string>("Action"),
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(
                  json.Value<long?>("time") ?? 0).UtcDateTime,
            TimeNano = json.Value<long?>("timeNano") ?? 0,
            Scope = json.Value<string>("scope"),
            RawJson = line
          };

          if (json["Actor"] is JObject actor)
          {
            evt.ActorId = actor.Value<string>("ID");
            evt.ActorAttributes = actor["Attributes"]?
                .ToObject<Dictionary<string, string>>()
                ?? new Dictionary<string, string>();
          }
        }
        catch
        {
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
      var path = $"/containers/{containerId}/stats?stream={stream}";

      await foreach (var line in ReadNdjsonStreamAsync(path, cancellationToken))
      {
        ContainerStats stats;
        try
        {
          stats = ParseContainerStats(line);
        }
        catch
        {
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
      var path = $"/containers/{containerId}/attach?" +
          $"stream=1" +
          $"&stdout={config.Stdout.ToString().ToLower()}" +
          $"&stderr={config.Stderr.ToString().ToLower()}" +
          $"&stdin={config.Stdin.ToString().ToLower()}";

      try
      {
        var content = new StringContent("", Encoding.UTF8);
        var stream = await Connection.PostStreamAsync(path, content, cancellationToken);

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
      var reader = new StreamReader(stream, Encoding.UTF8);

      while (!ct.IsCancellationRequested)
      {
        int bytesRead;
        try
        {
          bytesRead = await ReadExactAsync(stream, header, 8, ct);
        }
        catch (OperationCanceledException) { yield break; }
        catch { yield break; }

        if (bytesRead < 8)
        {
          // Possibly a raw (TTY) stream — try reading as plain text
          if (bytesRead > 0)
          {
            var partial = Encoding.UTF8.GetString(header, 0, bytesRead);
            var rest = await reader.ReadToEndAsync(ct);
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

        if (frameSize <= 0 || frameSize > 10 * 1024 * 1024) // 10MB safety limit
          yield break;

        var payload = new byte[frameSize];
        var payloadRead = await ReadExactAsync(stream, payload, frameSize, ct);
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
      var obj = JObject.Parse(json);
      var stats = new ContainerStats
      {
        ContainerId = obj.Value<string>("id"),
        Name = obj.Value<string>("name")?.TrimStart('/'),
        RawJson = json,
        Timestamp = obj.Value<DateTime?>("read") ?? DateTime.UtcNow
      };
      if (obj["cpu_stats"] is JObject cpuStats && obj["precpu_stats"] is JObject preCpuStats)
      {
        var cpuDelta = (cpuStats["cpu_usage"]?.Value<long?>("total_usage") ?? 0) -
            (preCpuStats["cpu_usage"]?.Value<long?>("total_usage") ?? 0);
        var systemDelta = (cpuStats.Value<long?>("system_cpu_usage") ?? 0) -
            (preCpuStats.Value<long?>("system_cpu_usage") ?? 0);
        var numCpus = cpuStats.Value<int?>("online_cpus") ?? 1;

        if (systemDelta > 0 && cpuDelta > 0)
          stats.CpuPercentage = (double)cpuDelta / systemDelta * numCpus * 100.0;
      }

      // Memory
      if (obj["memory_stats"] is JObject memStats)
      {
        stats.MemoryUsage = memStats.Value<long?>("usage") ?? 0;
        stats.MemoryLimit = memStats.Value<long?>("limit") ?? 0;
        if (stats.MemoryLimit > 0)
          stats.MemoryPercentage = (double)stats.MemoryUsage / stats.MemoryLimit * 100.0;
      }

      // Network
      if (obj["networks"] is JObject networks)
      {
        foreach (var prop in networks.Properties())
        {
          var net = prop.Value as JObject;
          stats.NetworkRx += net?.Value<long?>("rx_bytes") ?? 0;
          stats.NetworkTx += net?.Value<long?>("tx_bytes") ?? 0;
        }
      }

      // Block IO
      if (obj["blkio_stats"]?["io_service_bytes_recursive"] is JArray blkio)
      {
        foreach (var entry in blkio)
        {
          var op = entry.Value<string>("op")?.ToLower();
          var value = entry.Value<long?>("value") ?? 0;
          if (op == "read")
            stats.BlockRead += value;
          else if (op == "write")
            stats.BlockWrite += value;
        }
      }

      stats.Pids = obj["pids_stats"]?.Value<int?>("current") ?? 0;
      return stats;
    }

    #endregion
  }
}
