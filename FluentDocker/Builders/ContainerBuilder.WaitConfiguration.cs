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
      _waitPollIntervalMs = intervalMs;
      return this;
    }

    public IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Port,
        Target = portAndProto.Contains('/') ? portAndProto : $"{portAndProto}/tcp",
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Port,
        Target = portAndProto.Contains('/') ? portAndProto : $"{portAndProto}/tcp",
        Path = address,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000)
    {
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
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Http,
        Target = portAndProto.Contains('/') ? portAndProto : $"{portAndProto}/tcp",
        Path = path,
        TimeoutMs = timeoutMs,
        HttpMethod = HttpMethod.Get,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHttp(string url, long timeoutMs = 30000,
        HttpMethod method = null, string contentType = null, string body = null,
        Func<RequestResponse, int, long> continuation = null)
    {
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
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Healthy,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder Wait(Func<IContainerService, int, int> condition)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Lambda,
        LambdaCondition = condition,
        TimeoutMs = 60000,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    #endregion

  }
}
