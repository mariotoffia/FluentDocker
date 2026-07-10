using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// ContainerBuilder partial: lifecycle hooks and wait condition execution.
  /// </summary>
  internal sealed partial class ContainerBuilder
  {
    #region Lifecycle Hooks

    public IContainerBuilder CopyToOnStart(string hostPath, string containerPath)
    {
      _lifecycleHooks.Add(new LifecycleHook
      {
        Type = LifecycleHookType.CopyTo,
        TriggerState = ServiceRunningState.Running,
        HostPath = hostPath,
        ContainerPath = containerPath
      });
      return this;
    }

    public IContainerBuilder CopyFromOnDispose(string containerPath, string hostPath)
    {
      _lifecycleHooks.Add(new LifecycleHook
      {
        Type = LifecycleHookType.CopyFrom,
        TriggerState = ServiceRunningState.Removing,
        HostPath = hostPath,
        ContainerPath = containerPath
      });
      return this;
    }

    public IContainerBuilder ExportOnDispose(string hostPath, bool explode = false)
    {
      _lifecycleHooks.Add(new LifecycleHook
      {
        Type = LifecycleHookType.Export,
        TriggerState = ServiceRunningState.Removing,
        HostPath = hostPath,
        Explode = explode,
        Condition = _ => true
      });
      return this;
    }

    public IContainerBuilder ExportOnDispose(
        string hostPath, Func<IContainerService, bool> condition, bool explode = false)
    {
      _lifecycleHooks.Add(new LifecycleHook
      {
        Type = LifecycleHookType.Export,
        TriggerState = ServiceRunningState.Removing,
        HostPath = hostPath,
        Explode = explode,
        Condition = condition
      });
      return this;
    }

    public IContainerBuilder ExecuteOnRunning(params string[] command)
    {
      _lifecycleHooks.Add(new LifecycleHook
      {
        Type = LifecycleHookType.Execute,
        TriggerState = ServiceRunningState.Running,
        Command = command
      });
      return this;
    }

    public IContainerBuilder ExecuteOnDisposing(params string[] command)
    {
      _lifecycleHooks.Add(new LifecycleHook
      {
        Type = LifecycleHookType.Execute,
        TriggerState = ServiceRunningState.Removing,
        Command = command
      });
      return this;
    }

    #endregion

    /// <summary>
    /// Executes wait conditions that were deferred because the container had links.
    /// Called by Builder after all linked containers have been started.
    /// </summary>
    internal async Task ExecuteDeferredWaitConditionsAsync(CancellationToken cancellationToken)
    {
      if (_waitConditionsExecuted || _pendingService == null)
        return;

      _waitConditionsExecuted = true;
      await RunPostStartAsync(_pendingService, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the post-start sequence for a started container: setup hooks (CopyToOnStart) first,
    /// then wait conditions, then ExecuteOnRunning commands. Running Execute hooks AFTER the wait
    /// conditions means commands fire only once the container is actually ready (issue #283).
    /// </summary>
    internal async Task RunPostStartAsync(
        Services.Impl.ContainerService service, CancellationToken cancellationToken)
    {
      await RunRunningLifecycleHooksAsync(service, executeCommands: false, cancellationToken).ConfigureAwait(false);
      await ExecuteWaitConditionsAsync(service, cancellationToken).ConfigureAwait(false);
      await RunRunningLifecycleHooksAsync(service, executeCommands: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs Running-triggered lifecycle hooks. When <paramref name="executeCommands"/> is false,
    /// only setup hooks (CopyToOnStart) run; when true, only ExecuteOnRunning commands run.
    /// Execute hook command arrays are executed as one argv command, and failures propagate.
    /// </summary>
    private async Task RunRunningLifecycleHooksAsync(
        Services.Impl.ContainerService service, bool executeCommands,
        CancellationToken cancellationToken)
    {
      foreach (var hook in _lifecycleHooks)
      {
        if (hook.TriggerState != ServiceRunningState.Running)
          continue;

        switch (hook.Type)
        {
          case LifecycleHookType.CopyTo when !executeCommands:
            await service.CopyToAsync(hook.HostPath, hook.ContainerPath, cancellationToken).ConfigureAwait(false);
            break;
          case LifecycleHookType.Execute when executeCommands:
            if (hook.Command != null)
              await service.ExecuteAsync(hook.Command, cancellationToken).ConfigureAwait(false);
            break;
        }
      }
    }

    private async Task ExecuteWaitConditionsAsync(
        Services.Impl.ContainerService service, CancellationToken cancellationToken)
    {
      foreach (var condition in _waitConditions)
      {
        bool success;
        switch (condition.Type)
        {
          case WaitConditionType.Port:
            if (!string.IsNullOrEmpty(condition.Path))
            {
              var hostPort = await service.GetHostPortAsync(condition.Target, cancellationToken).ConfigureAwait(false);
              if (hostPort == 0)
                throw new FluentDockerException(
                    $"Port {condition.Target} is not exposed on container {service.Id}");
              success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                  condition.Path, hostPort, condition.TimeoutMs, condition.PollIntervalMs,
                  cancellationToken).ConfigureAwait(false);
            }
            else
            {
              success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                  service, condition.Target, condition.TimeoutMs, condition.PollIntervalMs,
                  cancellationToken).ConfigureAwait(false);
            }
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for port {condition.Target} on container {service.Id}");
            break;

          case WaitConditionType.Process:
            success = await Services.Extensions.ServiceExtensions.WaitForProcessAsync(
                service, condition.Target, condition.TimeoutMs, condition.PollIntervalMs,
                cancellationToken).ConfigureAwait(false);
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for process {condition.Target} on container {service.Id}");
            break;

          case WaitConditionType.Http:
            if (condition.Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                condition.Target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
              success = await WaitForHttpUrlAsync(condition.Target, condition.TimeoutMs,
                  condition.HttpMethod, condition.ContentType, condition.Body,
                  condition.HttpContinuation, condition.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
              success = await Services.Extensions.ServiceExtensions.WaitForHttpAsync(
                  service, condition.Target, condition.Path, condition.TimeoutMs,
                  condition.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for HTTP on container {service.Id}");
            break;

          case WaitConditionType.LogMessage:
            success = await Services.Extensions.ServiceExtensions.WaitForLogMessageAsync(
                service, condition.Target, condition.TimeoutMs, condition.PollIntervalMs,
                cancellationToken).ConfigureAwait(false);
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for log message '{condition.Target}' on container {service.Id}");
            break;

          case WaitConditionType.Healthy:
            success = await WaitForHealthyAsync(
                service, condition.TimeoutMs, condition.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for container {service.Id} to be healthy");
            break;

          case WaitConditionType.Lambda:
            success = await WaitForLambdaAsync(service, condition.LambdaCondition,
                condition.TimeoutMs, condition.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for custom condition on container {service.Id}");
            break;
        }
      }
    }

    private static async Task<bool> WaitForHealthyAsync(
        Services.Impl.ContainerService service, long timeoutMs,
        int pollIntervalMs, CancellationToken cancellationToken)
    {
      var sw = Stopwatch.StartNew();
      while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
      {
        service.InvalidateInspectCache();
        var config = await service.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (config?.State?.Health == null)
          throw new FluentDockerException(
              "Container has no HEALTHCHECK configured. " +
              "Use WaitForPort or WaitForLogMessage instead of WaitForHealthy.");

        var health = config.State.Health.Status;
        if (health == HealthState.Healthy)
          return true;
        if (health == HealthState.Unhealthy)
          throw new FluentDockerException(
              $"Container {service.Id} reported Unhealthy");

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }
      cancellationToken.ThrowIfCancellationRequested();
      return false;
    }

    private static async Task<bool> WaitForLambdaAsync(
        IContainerService service, Func<IContainerService, int, int> condition,
        long timeoutMs, int pollIntervalMs, CancellationToken cancellationToken)
    {
      var sw = Stopwatch.StartNew();
      var iteration = 0;
      while (sw.ElapsedMilliseconds < timeoutMs)
      {
        cancellationToken.ThrowIfCancellationRequested();
        int result;
        try
        {
          result = condition(service, iteration++);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (OperationCanceledException)
        {
          return false;
        }

        if (result < 0)
          return true;
        if (result == 0)
        {
          await Task.Delay(Math.Max(1, pollIntervalMs), cancellationToken).ConfigureAwait(false);
          continue;
        }
        await Task.Delay(result, cancellationToken).ConfigureAwait(false);
      }
      return false;
    }

    private static async Task<bool> WaitForHttpUrlAsync(
        string url, long timeoutMs, HttpMethod method, string contentType,
        string body, Func<RequestResponse, int, long> continuation,
        int pollIntervalMs, CancellationToken cancellationToken)
    {
      var sw = Stopwatch.StartNew();
      var iteration = 0;

      while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          var remainingMs = Math.Max(100, timeoutMs - sw.ElapsedMilliseconds);
          requestCts.CancelAfter(TimeSpan.FromMilliseconds(remainingMs));

          var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
          if (!string.IsNullOrEmpty(body))
          {
            request.Content = new StringContent(body);
            if (!string.IsNullOrEmpty(contentType))
              request.Content.Headers.ContentType =
                  new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
          }

          using var response = await Common.SharedHttpClient.Instance.SendAsync(request, requestCts.Token).ConfigureAwait(false);
          var responseBody = await response.Content.ReadAsStringAsync(requestCts.Token).ConfigureAwait(false);

          if (continuation != null)
          {
            var delay = continuation(
                new RequestResponse(response.Headers, response.StatusCode, responseBody, null),
                iteration++);
            if (delay < 0)
              return true;
            if (delay == 0)
            {
              await Task.Delay(Math.Max(1, pollIntervalMs), cancellationToken).ConfigureAwait(false);
              continue;
            }
            if (delay > 0)
            {
              await Task.Delay((int)Math.Min(delay, int.MaxValue), cancellationToken).ConfigureAwait(false);
              continue;
            }
          }
          else if (response.IsSuccessStatusCode)
          {
            return true;
          }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }
      cancellationToken.ThrowIfCancellationRequested();
      return false;
    }

    #region Container Helpers

    internal static async Task WaitForContainerStartedAsync(
        Drivers.IContainerDriver driver, Model.Drivers.DriverContext context,
        string containerId, string containerName, bool allowCleanExit, long timeoutMs, int pollIntervalMs,
        CancellationToken cancellationToken)
    {
      var sw = Stopwatch.StartNew();
      while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
      {
        var inspectResult = await driver.InspectAsync(context, containerId, cancellationToken).ConfigureAwait(false);
        if (inspectResult?.Success == true)
        {
          var state = inspectResult.Data?.State;
          // A crash-looping container reports Running=true while Restarting=true; keep polling
          // (it has not truly started) so WaitForRunning cannot succeed on a restarting container.
          if (state?.Running == true && state.Restarting != true)
            return;
          if (HasReachedTerminalState(state))
          {
            if (allowCleanExit && state.ExitCode == 0)
              return;

            var logs = await ReadLogTailAsync(driver, context, containerId, cancellationToken).ConfigureAwait(false);
            throw new FluentDockerException(AppendLogTail(
                $"Container {containerId} exited before it was ready with exit code {state.ExitCode}.",
                logs));
          }
        }
        else if (inspectResult?.ErrorCode == ErrorCodes.Container.NotFound)
        {
          var label = string.IsNullOrWhiteSpace(containerName) ? containerId : $"{containerName} ({containerId})";
          throw new FluentDockerException(
              $"Container {label} no longer exists (AutoRemove?) and may have exited before it was ready.");
        }
        else if (inspectResult?.Success == false)
        {
          throw new DriverException(
              $"Failed to inspect container {containerId} while waiting for start: {inspectResult.Error}",
              inspectResult.ErrorCode,
              inspectResult.ErrorContext);
        }
        var delay = (int)Math.Min(pollIntervalMs, Math.Max(1, timeoutMs - sw.ElapsedMilliseconds));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
      }
      cancellationToken.ThrowIfCancellationRequested();
      // Match the exit/AutoRemove branches above: use the friendly "name (id)" label and attach a
      // log tail, so a linked/deferred-start timeout is as diagnosable as a standalone one instead
      // of a bare "Timeout waiting for container <id>" — exactly the multi-container case where the
      // logs matter most (BLD-MAJ-5).
      var timeoutLabel = string.IsNullOrWhiteSpace(containerName) ? containerId : $"{containerName} ({containerId})";
      var timeoutLogs = await ReadLogTailAsync(driver, context, containerId, cancellationToken).ConfigureAwait(false);
      throw new FluentDockerException(AppendLogTail(
          $"Timeout waiting for container {timeoutLabel} to start.", timeoutLogs));
    }

    private static bool HasReachedTerminalState(ContainerState state)
    {
      if (state == null)
        return false;
      if (state.Dead)
        return true;
      return string.Equals(state.Status, "exited", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(state.Status, "dead", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadLogTailAsync(
        Drivers.IContainerDriver driver,
        Model.Drivers.DriverContext context,
        string containerId,
        CancellationToken cancellationToken)
    {
      try
      {
        var logs = await driver.GetLogsAsync(
            context, containerId, follow: false, tail: 100, timestamps: false, cancellationToken)
            .ConfigureAwait(false);
        return logs.Success ? logs.Data : null;
      }
      catch
      {
        return null;
      }
    }

    private static string AppendLogTail(string message, string logTail) =>
        string.IsNullOrWhiteSpace(logTail)
            ? message
            : $"{message}{Environment.NewLine}Container log tail:{Environment.NewLine}{logTail}";

    private static async Task<string> FindExistingContainerAsync(
        Drivers.IContainerDriver driver, Model.Drivers.DriverContext context,
        string name, CancellationToken cancellationToken)
    {
      var listResult = await driver.ListAsync(context,
          new Drivers.ContainerListFilter { All = true, Name = name }, cancellationToken).ConfigureAwait(false);
      if (!listResult.Success)
        return null;

      var normalizedName = name.StartsWith('/') ? name[1..] : name;
      var container = listResult.Data?.FirstOrDefault(c =>
      {
        var containerName = c.Name?.TrimStart('/');
        return string.Equals(containerName, normalizedName, StringComparison.Ordinal);
      });
      return container?.Id;
    }

    #endregion
  }
}
