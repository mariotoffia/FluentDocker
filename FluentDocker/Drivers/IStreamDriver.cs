using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Streaming operations for real-time data (logs, events, stats).
  /// Supported by: Docker, Podman, Kubernetes (partial)
  /// </summary>
  public interface IStreamDriver
  {
    /// <summary>
    /// Streams container logs.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="config">Stream configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of log lines</returns>
    IAsyncEnumerable<string> StreamLogsAsync(
        DriverContext context,
        string containerId,
        StreamLogsConfig config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams system events.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="config">Stream configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of events</returns>
    IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
        DriverContext context,
        StreamEventsConfig config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams container resource statistics.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name (null for all containers)</param>
    /// <param name="config">Stream configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of stats</returns>
    IAsyncEnumerable<ContainerStats> StreamStatsAsync(
        DriverContext context,
        string containerId = null,
        StreamStatsConfig config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches to a container's standard streams.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="containerId">Container ID or name</param>
    /// <param name="config">Attach configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Attach result with streams</returns>
    Task<CommandResponse<AttachResult>> AttachAsync(
        DriverContext context,
        string containerId,
        AttachConfig config = null,
        CancellationToken cancellationToken = default);
  }

  #region Config Types

  /// <summary>
  /// Configuration for streaming logs.
  /// </summary>
  public class StreamLogsConfig
  {
    /// <summary>Follow log output (stream continuously).</summary>
    public bool Follow { get; set; } = true;

    /// <summary>Show timestamps.</summary>
    public bool Timestamps { get; set; }

    /// <summary>Show logs since timestamp (RFC3339 or relative).</summary>
    public string Since { get; set; }

    /// <summary>Show logs until timestamp.</summary>
    public string Until { get; set; }

    /// <summary>Number of lines to show from end (null = all).</summary>
    public int? Tail { get; set; }

    /// <summary>Show stdout.</summary>
    public bool Stdout { get; set; } = true;

    /// <summary>Show stderr.</summary>
    public bool Stderr { get; set; } = true;

    /// <summary>Show extra details.</summary>
    public bool Details { get; set; }
  }

  /// <summary>
  /// Configuration for streaming events.
  /// </summary>
  public class StreamEventsConfig
  {
    /// <summary>Show events since timestamp.</summary>
    public string Since { get; set; }

    /// <summary>Show events until timestamp.</summary>
    public string Until { get; set; }

    /// <summary>Filter events by type (container, image, volume, network, daemon).</summary>
    public List<string> Types { get; set; } = [];

    /// <summary>Filter events by action (create, start, stop, etc.).</summary>
    public List<string> Actions { get; set; } = [];

    /// <summary>Filter by container/image/network/etc. name or ID.</summary>
    public Dictionary<string, string> Filters { get; set; } = [];
  }

  /// <summary>
  /// Configuration for streaming stats.
  /// </summary>
  public class StreamStatsConfig
  {
    /// <summary>Stream stats continuously (vs one-shot).</summary>
    public bool Stream { get; set; } = true;

    /// <summary>Don't include column headers.</summary>
    public bool NoHeader { get; set; }

    /// <summary>Show all containers (default: running only).</summary>
    public bool All { get; set; }
  }

  /// <summary>
  /// Configuration for attach operation.
  /// </summary>
  public class AttachConfig
  {
    /// <summary>Attach to stdout.</summary>
    public bool Stdout { get; set; } = true;

    /// <summary>Attach to stderr.</summary>
    public bool Stderr { get; set; } = true;

    /// <summary>Attach to stdin.</summary>
    public bool Stdin { get; set; }

    /// <summary>Allocate a pseudo-TTY.</summary>
    public bool Tty { get; set; }

    /// <summary>Key sequence for detaching.</summary>
    public string DetachKeys { get; set; }

    /// <summary>Do not attach stdout.</summary>
    public bool NoStdout { get; set; }

    /// <summary>Do not attach stderr.</summary>
    public bool NoStderr { get; set; }

    /// <summary>Proxy all received signals.</summary>
    public bool SigProxy { get; set; } = true;
  }

  #endregion

  #region Event/Stats Types

  /// <summary>
  /// Represents a Docker/Podman system event.
  /// </summary>
  public class ContainerEvent
  {
    /// <summary>Event type (container, image, network, volume, daemon).</summary>
    public string Type { get; set; }

    /// <summary>Event action (create, start, stop, die, etc.).</summary>
    public string Action { get; set; }

    /// <summary>Actor ID (container ID, image ID, etc.).</summary>
    public string ActorId { get; set; }

    /// <summary>Actor attributes.</summary>
    public Dictionary<string, string> ActorAttributes { get; set; } = [];

    /// <summary>Timestamp of the event.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Unix timestamp (nanoseconds).</summary>
    public long TimeNano { get; set; }

    /// <summary>Scope of the event (local, swarm).</summary>
    public string Scope { get; set; }

    /// <summary>Raw JSON string of the event.</summary>
    public string RawJson { get; set; }
  }

  /// <summary>
  /// Represents container resource statistics.
  /// </summary>
  public class ContainerStats
  {
    /// <summary>Container ID.</summary>
    public string ContainerId { get; set; }

    /// <summary>Container name.</summary>
    public string Name { get; set; }

    /// <summary>CPU usage percentage.</summary>
    public double CpuPercentage { get; set; }

    /// <summary>Memory usage in bytes.</summary>
    public long MemoryUsage { get; set; }

    /// <summary>Memory limit in bytes.</summary>
    public long MemoryLimit { get; set; }

    /// <summary>Memory usage percentage.</summary>
    public double MemoryPercentage { get; set; }

    /// <summary>Network I/O received in bytes.</summary>
    public long NetworkRx { get; set; }

    /// <summary>Network I/O transmitted in bytes.</summary>
    public long NetworkTx { get; set; }

    /// <summary>Block I/O read in bytes.</summary>
    public long BlockRead { get; set; }

    /// <summary>Block I/O written in bytes.</summary>
    public long BlockWrite { get; set; }

    /// <summary>Number of PIDs.</summary>
    public int Pids { get; set; }

    /// <summary>Timestamp of the stats.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Raw JSON string of stats.</summary>
    public string RawJson { get; set; }
  }

  /// <summary>
  /// Result of attach operation.
  /// </summary>
  public class AttachResult : IAsyncDisposable
  {
    /// <summary>Input stream (to send data to container).</summary>
    public Stream InputStream { get; set; }

    /// <summary>Output stream (to read data from container).</summary>
    public Stream OutputStream { get; set; }

    /// <summary>Error stream (to read error data from container).</summary>
    public Stream ErrorStream { get; set; }

    /// <summary>Whether the attach is still connected.</summary>
    public bool IsConnected { get; set; }

    /// <summary>The underlying process for CLI-based attach (used for cleanup).</summary>
    internal Process AttachedProcess { get; set; }

    /// <summary>Disposes the attach connection.</summary>
    public ValueTask DisposeAsync()
    {
      InputStream?.Dispose();
      OutputStream?.Dispose();
      ErrorStream?.Dispose();
      IsConnected = false;

      if (AttachedProcess != null && !AttachedProcess.HasExited)
      {
        try
        { AttachedProcess.Kill(); }
        catch (Exception ex) { NullLogger.Instance.LogWarning(ex, "Process kill failed"); }
        AttachedProcess.Dispose();
      }

      GC.SuppressFinalize(this);
      return ValueTask.CompletedTask;
    }
  }

  #endregion
}

