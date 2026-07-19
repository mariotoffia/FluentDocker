#nullable disable warnings
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;

namespace FluentDocker.Services
{
  /// <summary>
  /// Shared diagnostics for container wait loops: terminal-state detection and best-effort
  /// log-tail capture. Used by both the container-start gate
  /// (<c>Builders.ContainerBuilder.WaitForContainerStartedAsync</c>) and the post-start wait
  /// extensions (<see cref="Extensions.ServiceExtensions"/>) so a crashed container fails fast
  /// with an exit code and log tail instead of burning the full wait timeout.
  /// </summary>
  internal static class WaitDiagnostics
  {
    private const int LogTailLines = 100;
    private const string LogTailMarker = "Container log tail:";

    /// <summary>
    /// True when <paramref name="state"/> reports a terminal (exited/dead) container state.
    /// A crash-looping container reports status "restarting" (Docker's dedicated status for a
    /// container the runtime is about to retry), never "exited"/"dead", so this naturally
    /// returns false while the runtime still intends to restart it.
    /// </summary>
    internal static bool HasReachedTerminalState(ContainerState state)
    {
      if (state == null)
        return false;
      if (state.Dead)
        return true;
      return string.Equals(state.Status, "exited", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(state.Status, "dead", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Appends a log tail to <paramref name="message"/> when one was captured. Idempotent: a
    /// message that already carries a log tail (e.g. an exception thrown by
    /// <see cref="ThrowIfTerminalAsync"/> or the start gate, then re-wrapped by a builder catch)
    /// is returned unchanged so the tail is never duplicated.
    /// </summary>
    internal static string AppendLogTail(string message, string logTail) =>
        string.IsNullOrWhiteSpace(logTail) ||
        message?.Contains(LogTailMarker, StringComparison.Ordinal) == true
            ? message
            : $"{message}{Environment.NewLine}{LogTailMarker}{Environment.NewLine}{logTail}";

    /// <summary>Best-effort log tail read via a raw driver; returns null on any failure.</summary>
    internal static async Task<string> ReadLogTailAsync(
        IContainerDriver driver,
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken)
    {
      try
      {
        var logs = await driver.GetLogsAsync(
            context, containerId, follow: false, tail: LogTailLines, timestamps: false, cancellationToken)
            .ConfigureAwait(false);
        return logs.Success ? logs.Data : null;
      }
      catch
      {
        return null;
      }
    }

    /// <summary>Best-effort log tail read via a container service; returns null on any failure.</summary>
    internal static async Task<string> ReadLogTailAsync(
        IContainerService service,
        CancellationToken cancellationToken)
    {
      try
      {
        return service is ContainerService containerService
            ? await containerService.GetLogsTailAsync(LogTailLines, cancellationToken).ConfigureAwait(false)
            : await service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Inspects <paramref name="service"/>'s container and throws a
    /// <see cref="FluentDockerException"/> carrying the exit code and a log tail when it has
    /// reached a terminal (exited/dead) state. Inspect failures are swallowed - the state is
    /// simply unknown - so a wait loop's timeout/retry behavior is never changed unless this can
    /// positively confirm the container has died; callers that need this fail-fast diagnostic
    /// call it once per poll iteration, outside their own transient-retry catch blocks, so the
    /// thrown exception always propagates.
    /// </summary>
    internal static async Task ThrowIfTerminalAsync(
        IContainerService service, CancellationToken cancellationToken)
    {
      Container container;
      try
      {
        container = await service.InspectAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch
      {
        return;
      }

      var state = container?.State;
      if (!HasReachedTerminalState(state))
        return;

      var logTail = await ReadLogTailAsync(service, cancellationToken).ConfigureAwait(false);
      throw new FluentDockerException(AppendLogTail(
          $"Container {service.Id} exited while waiting with exit code {state.ExitCode}.",
          logTail));
    }
  }
}
