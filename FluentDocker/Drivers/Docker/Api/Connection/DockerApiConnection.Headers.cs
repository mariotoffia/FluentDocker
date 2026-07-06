using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  public sealed partial class DockerApiConnection
  {
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
      using var ttfbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      ttfbCts.CancelAfter(_config.ConnectionTimeout);
      return await _longRunningHttpClient.SendAsync(
          request, HttpCompletionOption.ResponseHeadersRead, ttfbCts.Token)
          .ConfigureAwait(false);
    }
  }
}
