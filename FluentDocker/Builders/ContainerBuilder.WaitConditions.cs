using System;
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
    internal partial class ContainerBuilder
    {
        /// <summary>
        /// Executes wait conditions that were deferred because the container had links.
        /// Called by Builder after all linked containers have been started.
        /// </summary>
        internal async Task ExecuteDeferredWaitConditionsAsync(CancellationToken cancellationToken)
        {
            if (_waitConditionsExecuted || _pendingService == null)
                return;

            _waitConditionsExecuted = true;
            await ExecuteWaitConditionsAsync(_pendingService, cancellationToken);
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
                            File.ReadAllBytes(hook.HostPath), cancellationToken);
                        break;
                    case LifecycleHookType.Execute:
                        await service.ExecuteAsync(string.Join(" ", hook.Command), cancellationToken);
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
                            var hostPort = await service.GetHostPortAsync(condition.Target, cancellationToken);
                            success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                                condition.Path, hostPort, condition.TimeoutMs, cancellationToken);
                        }
                        else
                        {
                            success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                                service, condition.Target, condition.TimeoutMs, cancellationToken);
                        }
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for port {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.Process:
                        success = await Services.Extensions.ServiceExtensions.WaitForProcessAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for process {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.Http:
                        if (condition.Target.StartsWith("http://") || condition.Target.StartsWith("https://"))
                        {
                            success = await WaitForHttpUrlAsync(condition.Target, condition.TimeoutMs,
                                condition.HttpMethod, condition.ContentType, condition.Body,
                                condition.HttpContinuation, cancellationToken);
                        }
                        else
                        {
                            success = await Services.Extensions.ServiceExtensions.WaitForHttpAsync(
                                service, condition.Target, condition.Path, condition.TimeoutMs, cancellationToken);
                        }
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for HTTP on container {service.Id}");
                        break;

                    case WaitConditionType.LogMessage:
                        success = await Services.Extensions.ServiceExtensions.WaitForLogMessageAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for log message '{condition.Target}' on container {service.Id}");
                        break;

                    case WaitConditionType.Healthy:
                        success = await WaitForHealthyAsync(service, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for container {service.Id} to be healthy");
                        break;

                    case WaitConditionType.Lambda:
                        success = await WaitForLambdaAsync(service, condition.LambdaCondition,
                            condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for custom condition on container {service.Id}");
                        break;
                }
            }
        }

        private async Task<bool> WaitForHealthyAsync(
            Services.Impl.ContainerService service, long timeoutMs, CancellationToken cancellationToken)
        {
            var start = DateTime.UtcNow;
            var elapsed = 0L;
            while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
            {
                var config = await service.InspectAsync(cancellationToken);
                var health = config?.State?.Health?.Status;
                if (health == HealthState.Healthy) return true;
                if (health == HealthState.Unhealthy) return false;
                await Task.Delay(1000, cancellationToken);
                elapsed = (long)(DateTime.UtcNow - start).TotalMilliseconds;
            }
            return false;
        }

        private async Task<bool> WaitForLambdaAsync(
            IContainerService service, Func<IContainerService, int, int> condition,
            long timeoutMs, CancellationToken cancellationToken)
        {
            var start = DateTime.UtcNow;
            var elapsed = 0L;
            var iteration = 0;
            while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
            {
                var result = condition(service, iteration++);
                if (result < 0) return true;
                if (result == 0) continue;
                await Task.Delay(result, cancellationToken);
                elapsed = (long)(DateTime.UtcNow - start).TotalMilliseconds;
            }
            return false;
        }

        private async Task<bool> WaitForHttpUrlAsync(
            string url, long timeoutMs, HttpMethod method, string contentType,
            string body, Func<RequestResponse, int, long> continuation,
            CancellationToken cancellationToken)
        {
            var start = DateTime.UtcNow;
            var elapsed = 0L;
            var iteration = 0;

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
                    if (!string.IsNullOrEmpty(body))
                    {
                        request.Content = new StringContent(body);
                        if (!string.IsNullOrEmpty(contentType))
                            request.Content.Headers.ContentType =
                                new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    }

                    var response = await httpClient.SendAsync(request, cancellationToken);
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (continuation != null)
                    {
                        var delay = continuation(
                            new RequestResponse(response.Headers, response.StatusCode, responseBody, null),
                            iteration++);
                        if (delay < 0) return true;
                        if (delay > 0) await Task.Delay((int)delay, cancellationToken);
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }

                await Task.Delay(500, cancellationToken);
                elapsed = (long)(DateTime.UtcNow - start).TotalMilliseconds;
            }
            return false;
        }
    }
}
