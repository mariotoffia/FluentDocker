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
        // ponytail: body-bearing requests (build ctx, image load/import) legitimately exceed the
        // TTFB cap, so only the connect phase (SocketsHttpHandler.ConnectTimeout) and the caller's
        // token bound them — no mid-upload stall timeout by design. Add an absolute cap if a stalled
        // daemon mid-upload with CancellationToken.None proves a real problem.
        return await _longRunningHttpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
      }

      using var ttfbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      ttfbCts.CancelAfter(_config.ConnectionTimeout);
      return await _longRunningHttpClient.SendAsync(
          request, HttpCompletionOption.ResponseHeadersRead, ttfbCts.Token)
          .ConfigureAwait(false);
    }
  }
}
