using System;
using System.Net.Http;
using FluentDocker.Common;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// ContainerBuilder partial: public wait-condition configuration methods.
  /// </summary>
  internal sealed partial class ContainerBuilder
  {
    #region Wait Conditions

    public IContainerBuilder WithWaitPollInterval(int intervalMs)
    {
      if (intervalMs < 1)
        throw new FluentDockerException("Wait poll interval must be at least 1 ms.");
      _waitPollIntervalMs = intervalMs;
      return this;
    }

    public IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Port,
        Target = NormalizeContainerPort(portAndProto),
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Port,
        Target = NormalizeContainerPort(portAndProto),
        Path = address,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Process,
        Target = processName,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Http,
        Target = NormalizeContainerPort(portAndProto),
        Path = path,
        TimeoutMs = timeoutMs,
        HttpMethod = HttpMethod.Get,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHttp(string portAndProto, long timeoutMs) =>
        WaitForHttp(portAndProto, "/", timeoutMs);

    public IContainerBuilder WaitForHttpUrl(string url, long timeoutMs = 30000,
        HttpMethod? method = null, string? contentType = null, string? body = null,
        Func<RequestResponse, int, long>? continuation = null)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Http,
        Target = url,
        TimeoutMs = timeoutMs,
        HttpMethod = method ?? HttpMethod.Get,
        ContentType = contentType,
        Body = body,
        HttpContinuation = continuation,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.LogMessage,
        Target = message,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHealthy(long timeoutMs = 30000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Healthy,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder Wait(
        Func<IContainerService, int, int> condition,
        long timeoutMs = 60000)
    {
      ValidateWaitTimeout(timeoutMs);
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Lambda,
        LambdaCondition = condition,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    private static void ValidateWaitTimeout(long timeoutMs)
    {
      if (timeoutMs < 1)
        throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "Value must be positive.");
    }

    #endregion

  }
}
