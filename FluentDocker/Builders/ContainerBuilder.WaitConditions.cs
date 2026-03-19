using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
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
      await ExecuteWaitConditionsAsync(_pendingService, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteLifecycleHooksAsync(
        Services.Impl.ContainerService service, ServiceRunningState state,
        CancellationToken cancellationToken)
    {
      var hooks = _lifecycleHooks.Where(h => h.TriggerState == state);
      foreach (var hook in hooks)
      {
        switch (hook.Type)
        {
          case LifecycleHookType.CopyTo:
            await service.CopyToAsync(hook.ContainerPath,
                File.ReadAllBytes(hook.HostPath), cancellationToken).ConfigureAwait(false);
            break;
          case LifecycleHookType.Execute:
            await service.ExecuteAsync(string.Join(" ", hook.Command), cancellationToken).ConfigureAwait(false);
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
              success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                  condition.Path, hostPort, condition.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
              success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                  service, condition.Target, condition.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for port {condition.Target} on container {service.Id}");
            break;

          case WaitConditionType.Process:
            success = await Services.Extensions.ServiceExtensions.WaitForProcessAsync(
                service, condition.Target, condition.TimeoutMs, cancellationToken).ConfigureAwait(false);
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for process {condition.Target} on container {service.Id}");
            break;

          case WaitConditionType.Http:
            if (condition.Target.StartsWith("http://") || condition.Target.StartsWith("https://"))
            {
              success = await WaitForHttpUrlAsync(condition.Target, condition.TimeoutMs,
                  condition.HttpMethod, condition.ContentType, condition.Body,
                  condition.HttpContinuation, condition.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
              success = await Services.Extensions.ServiceExtensions.WaitForHttpAsync(
                  service, condition.Target, condition.Path, condition.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            if (!success)
              throw new FluentDockerException(
                  $"Timeout waiting for HTTP on container {service.Id}");
            break;

          case WaitConditionType.LogMessage:
            success = await Services.Extensions.ServiceExtensions.WaitForLogMessageAsync(
                service, condition.Target, condition.TimeoutMs, cancellationToken).ConfigureAwait(false);
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
                condition.TimeoutMs, cancellationToken).ConfigureAwait(false);
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
        var config = await service.InspectAsync(cancellationToken).ConfigureAwait(false);
        var health = config?.State?.Health?.Status;
        if (health == HealthState.Healthy)
          return true;
        if (health == HealthState.Unhealthy)
          return false;
        // Fast-fail: no HEALTHCHECK configured on the image
        if (health == null || health == HealthState.Unknown)
          return false;
        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }
      return false;
    }

    private static async Task<bool> WaitForLambdaAsync(
        IContainerService service, Func<IContainerService, int, int> condition,
        long timeoutMs, CancellationToken cancellationToken)
    {
      var sw = Stopwatch.StartNew();
      var iteration = 0;
      try
      {
        while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
          var result = condition(service, iteration++);
          if (result < 0)
            return true;
          if (result == 0)
          { await Task.Yield(); continue; }
          await Task.Delay(result, cancellationToken).ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) { }
      return false;
    }

    private static readonly HttpClient s_httpClient = new()
    {
      Timeout = Timeout.InfiniteTimeSpan
    };

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

          var response = await s_httpClient.SendAsync(request, requestCts.Token).ConfigureAwait(false);
          var responseBody = await response.Content.ReadAsStringAsync(requestCts.Token).ConfigureAwait(false);

          if (continuation != null)
          {
            var delay = continuation(
                new RequestResponse(response.Headers, response.StatusCode, responseBody, null),
                iteration++);
            if (delay < 0)
              return true;
            if (delay > 0)
              await Task.Delay((int)delay, cancellationToken).ConfigureAwait(false);
          }
          else if (response.IsSuccessStatusCode)
          {
            return true;
          }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }
      return false;
    }

    #region Container Helpers

    private static async Task WaitForContainerRunningAsync(
        Drivers.IContainerDriver driver, Model.Drivers.DriverContext context,
        string containerId, CancellationToken cancellationToken)
    {
      const int maxAttempts = 30;
      const int delayMs = 100;
      for (var i = 0; i < maxAttempts; i++)
      {
        var inspectResult = await driver.InspectAsync(context, containerId, cancellationToken).ConfigureAwait(false);
        if (inspectResult.Success && inspectResult.Data?.State?.Running == true)
          return;
        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
      }
    }

    private static async Task<string> FindExistingContainerAsync(
        Drivers.IContainerDriver driver, Model.Drivers.DriverContext context,
        string name, CancellationToken cancellationToken)
    {
      var listResult = await driver.ListAsync(context,
          new Drivers.ContainerListFilter { All = true, Name = name }, cancellationToken).ConfigureAwait(false);
      if (!listResult.Success)
        return null;

      var normalizedName = name.StartsWith('/') ? name.Substring(1) : name;
      var container = listResult.Data?.FirstOrDefault(c =>
      {
        var containerName = c.Name?.TrimStart('/');
        return string.Equals(containerName, normalizedName, StringComparison.OrdinalIgnoreCase);
      });
      return container?.Id;
    }

    #endregion
  }
}
