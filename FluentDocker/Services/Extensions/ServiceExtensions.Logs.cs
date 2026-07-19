#nullable disable warnings
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;

namespace FluentDocker.Services.Extensions
{
  public static partial class ServiceExtensions
  {
    /// <summary>
    /// Waits for container logs to contain specific text.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="text">Text to search for in logs.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the text was found, false if timeout.</returns>
    /// <remarks>
    /// Extension waits return false on timeout and throw cancellation or non-transient
    /// driver errors. Builder waits throw <see cref="FluentDockerException"/>. Fails fast
    /// when the container reaches a terminal state (exited/dead) before the message is seen:
    /// throws <see cref="FluentDockerException"/> with the exit code and a log tail rather
    /// than polling to timeout. The log content is checked before the terminal-state check,
    /// so a message logged by a short-lived container that then exits still returns true.
    /// </remarks>
    public static async Task<bool> WaitForLogMessageAsync(
        this IContainerService service,
        string text,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      return await WaitForLogMessageAsync(service, text, timeout, 500, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for container logs to contain text, polling at the specified interval.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="text">Text to search for in logs.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="pollIntervalMs">Milliseconds to wait between log polls.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the text was found, false if timeout.</returns>
    public static async Task<bool> WaitForLogMessageAsync(
        this IContainerService service,
        string text,
        long timeout,
        int pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();
      var firstLogPoll = true;
      var pollCount = 0;

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          var containerService = service as ContainerService;
          var useTail = containerService != null &&
              !firstLogPoll &&
              pollCount % 10 != 0;
          var logs = useTail
              ? await containerService.GetLogsTailAsync(LogTailLines, cancellationToken).ConfigureAwait(false)
              : await service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
          firstLogPoll = false;
          pollCount++;
          if (logs?.Contains(text) == true)
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
        }
        catch (Exception ex) when (IsRetriableWaitException(ex, cancellationToken))
        {
          LogDebug(service, ex, "WaitForLogMessageAsync", text);
        }

        // Message not yet present: fail fast if the container has died. Checked AFTER the content
        // scan (unlike the port/http waits) so a message logged just before a short-lived container
        // exits still returns true above. Outside the catches so the diagnostic exception propagates.
        await WaitDiagnostics.ThrowIfTerminalAsync(service, cancellationToken).ConfigureAwait(false);

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      return await ContainsLogMessageAsync(service, text, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ContainsLogMessageAsync(
        IContainerService service,
        string text,
        CancellationToken cancellationToken)
    {
      if (service is ContainerService containerService &&
          containerService.Kernel.TrySysCtl<IStreamDriver>(containerService.DriverId, out var streamDriver))
      {
        try
        {
          var context = new DriverContext(containerService.DriverId);
          var config = new StreamLogsConfig { Follow = false };
          await foreach (var line in streamDriver.StreamLogsAsync(
                  context, containerService.Id, config, cancellationToken)
              .WithCancellation(cancellationToken).ConfigureAwait(false))
          {
            if (line?.Contains(text) == true)
              return true;
          }

          return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
          return false;
        }
        catch (Exception ex) when (IsRetriableWaitException(ex, cancellationToken))
        {
          LogDebug(service, ex, "WaitForLogMessageAsync", text);
          return false;
        }
      }

      try
      {
        var logs = await service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
        return logs?.Contains(text) == true;
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (DriverException ex) when (ex.IsTransient)
      {
        return false;
      }
      catch (Exception ex) when (IsRetriableWaitException(ex, cancellationToken))
      {
        LogDebug(service, ex, "WaitForLogMessageAsync", text);
        return false;
      }
    }
  }
}
