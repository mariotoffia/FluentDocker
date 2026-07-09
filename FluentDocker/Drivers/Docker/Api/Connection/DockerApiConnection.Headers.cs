using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  public sealed partial class DockerApiConnection
  {
    /// <inheritdoc />
    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      using var request = new HttpRequestMessage(HttpMethod.Post, versionedPath)
      {
        Content = content
      };
      if (headers != null)
      {
        foreach (var header in headers)
          request.Headers.TryAddWithoutValidation(header.Key, header.Value);
      }

      var client = UseLongRunningPostClient(versionedPath) ? _longRunningHttpClient : _httpClient;
      return await client.SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendForHeadersAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
      if (request.Content != null)
      {
        // DAPI-3: bound the upload against mid-transfer write stalls. A healthy upload makes
        // sub-ConnectionTimeout write progress continuously; a daemon that wedges while we are still
        // streaming the body stops draining the socket, our writes stall, and the watchdog cancels the
        // send instead of hanging forever (even under CancellationToken.None).
        // ponytail: the watchdog bounds only the body-writing phase. Once the whole body is flushed
        // (uploadCompleted) it disarms, and the post-upload response-header wait is left to the caller
        // token: a large `load`/`import` can legitimately take longer than ConnectionTimeout to produce
        // headers, so bounding it here would false-positive. Ceiling: a daemon that accepts a small body
        // in full and then wedges before responding is unbounded under CancellationToken.None — out of
        // scope for this watchdog; pass a token if you need to bound that.
        // ponytail: a spurious watchdog cancel cannot corrupt a returned response at realistic bounds —
        // the finally awaits the watchdog to completion before returning, and at the default 30s bound an
        // early response arrives in milliseconds, long before a full bound of write-stall accrues. Only a
        // pathologically small bound could race a just-arrived streaming response; that is the ceiling.
        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watchdogDone = new CancellationTokenSource();
        var lastProgress = Environment.TickCount64;
        var uploadCompleted = 0;
        request.Content = new ProgressObservingHttpContent(
            request.Content,
            () => Interlocked.Exchange(ref lastProgress, Environment.TickCount64),
            () => Volatile.Write(ref uploadCompleted, 1));
        var bound = _config.ConnectionTimeout;
        var watchdog = UploadStallWatchdogAsync(
            () => Interlocked.Read(ref lastProgress),
            () => Volatile.Read(ref uploadCompleted) != 0,
            bound, stallCts, watchdogDone.Token);
        try
        {
          return await _longRunningHttpClient.SendAsync(
              request, HttpCompletionOption.ResponseHeadersRead, stallCts.Token)
              .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (
            !ct.IsCancellationRequested && stallCts.IsCancellationRequested)
        {
          throw new DockerApiUploadStallException(bound, ex);
        }
        finally
        {
          watchdogDone.Cancel();
          await watchdog.ConfigureAwait(false);
        }
      }

      using var ttfbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      ttfbCts.CancelAfter(_config.ConnectionTimeout);
      try
      {
        return await _longRunningHttpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ttfbCts.Token)
            .ConfigureAwait(false);
      }
      catch (OperationCanceledException ex) when (
          !ct.IsCancellationRequested && ttfbCts.IsCancellationRequested)
      {
        throw new DockerApiTtfbTimeoutException(_config.ConnectionTimeout, ex);
      }
    }

    private static async Task UploadStallWatchdogAsync(
        Func<long> lastProgress, Func<bool> uploadCompleted, TimeSpan bound,
        CancellationTokenSource stallCts, CancellationToken done)
    {
      var boundMs = (long)bound.TotalMilliseconds;
      var pollMs = Math.Clamp(boundMs / 4, 25, 1000);
      try
      {
        while (!done.IsCancellationRequested)
        {
          await Task.Delay(TimeSpan.FromMilliseconds(pollMs), done).ConfigureAwait(false);
          if (!uploadCompleted() && Environment.TickCount64 - lastProgress() > boundMs)
          {
            stallCts.Cancel();
            return;
          }
        }
      }
      catch (OperationCanceledException)
      {
      }
    }
  }

  internal sealed class DockerApiTtfbTimeoutException : TaskCanceledException
  {
    public DockerApiTtfbTimeoutException(TimeSpan timeout, Exception innerException)
        : base($"Docker API connection/TTFB timed out after {timeout}", innerException)
        => Timeout = timeout;

    public TimeSpan Timeout { get; }
  }

  internal sealed class DockerApiUploadStallException : TaskCanceledException
  {
    public DockerApiUploadStallException(TimeSpan timeout, Exception innerException)
        : base($"Docker API upload stalled: no write progress for {timeout}", innerException)
        => Timeout = timeout;

    public TimeSpan Timeout { get; }
  }
}
