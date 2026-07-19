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
        // The watchdog bounds the body-writing phase with ConnectionTimeout; once the whole body is
        // flushed (uploadCompleted) it re-arms with the larger RequestTimeout to bound the response-
        // header wait too — otherwise a daemon that drains the context tar then wedges before
        // responding hangs FOREVER under CancellationToken.None, an un-diagnosable production hang on
        // the heaviest flows (build/load/import) (DAPI-MAJ-2). RequestTimeout (minutes) is generous
        // enough that a legitimately-slow large load still produces headers before it fires.
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
        var responseHeaderBound = _config.RequestTimeout;
        var watchdog = UploadStallWatchdogAsync(
            () => Interlocked.Read(ref lastProgress),
            () => Volatile.Read(ref uploadCompleted) != 0,
            bound, responseHeaderBound, stallCts, watchdogDone.Token);
        try
        {
          return await _longRunningHttpClient.SendAsync(
              request, HttpCompletionOption.ResponseHeadersRead, stallCts.Token)
              .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (
            !ct.IsCancellationRequested && stallCts.IsCancellationRequested)
        {
          // Distinguish the two watchdog cancel causes: after the body flushed, a cancel is the
          // post-upload response-header timeout (RequestTimeout); before, it is a mid-upload stall.
          if (Volatile.Read(ref uploadCompleted) != 0)
            throw new DockerApiTtfbTimeoutException(responseHeaderBound, ex);
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
        TimeSpan responseHeaderBound, CancellationTokenSource stallCts, CancellationToken done)
    {
      // An infinite bound means "no stall watchdog": keep polling only to observe upload completion
      // (so the response-header wait can still be armed) and never cancel for a write stall. Without
      // this, boundMs would be -1 and the elapsed-since-progress check below would fire on the first
      // poll (~25 ms), cancelling every body-bearing upload (DAPI-3).
      var infinite = bound == Timeout.InfiniteTimeSpan;
      var boundMs = (long)bound.TotalMilliseconds;
      var pollMs = infinite ? 1000 : Math.Clamp(boundMs / 4, 25, 1000);
      try
      {
        while (!done.IsCancellationRequested)
        {
          await Task.Delay(TimeSpan.FromMilliseconds(pollMs), done).ConfigureAwait(false);
          if (uploadCompleted())
          {
            // Body flushed: re-arm to bound the response-header wait, then stop watching for stalls.
            stallCts.CancelAfter(responseHeaderBound);
            return;
          }

          if (!infinite && Environment.TickCount64 - lastProgress() > boundMs)
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
