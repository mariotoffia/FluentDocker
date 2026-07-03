using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using Microsoft.Extensions.Logging;

// ReSharper disable IdentifierTypo

namespace FluentDocker.Extensions
{
  public static class HttpExtensions
  {
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static HttpClient Client => SharedHttpClient.Instance;

    /// <summary>
    /// Downloads a resource to local path and filename.
    /// </summary>
    /// <param name="url">The url to resource to download.</param>
    /// <param name="fqPath">The fully qualified path to the file where the data is stored.</param>
    /// <returns></returns>
    public static async Task<bool> Download(this Uri url, string fqPath)
    {
      return await Download(url, fqPath, DefaultRequestTimeout).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a resource to local path and filename.
    /// </summary>
    /// <param name="url">The url to resource to download.</param>
    /// <param name="fqPath">The fully qualified path to the file where the data is stored.</param>
    /// <param name="timeout">The per-request timeout.</param>
    /// <returns></returns>
    public static async Task<bool> Download(this Uri url, string fqPath, TimeSpan timeout)
    {
      using var requestCts = CreateTimeoutSource(timeout);
      var response = await Client.GetAsync(url, requestCts.Token).ConfigureAwait(false);

      if (response.IsSuccessStatusCode)
      {
        using var fs = new FileStream(fqPath, FileMode.Create);
        await response.Content.CopyToAsync(fs, requestCts.Token).ConfigureAwait(false);
        await fs.FlushAsync(requestCts.Token).ConfigureAwait(false);
      }
      else
      {
        throw new FluentDockerException(
          $"Could not download file {url} code: {response.StatusCode}"
        );
      }

      return true;
    }

    /// <summary>
    /// Gets a body from a URL.
    /// </summary>
    /// <param name="url">The url including query parameters.</param>
    /// <returns>A response string, or <see cref="string.Empty"/> if any errors occurs.</returns>
    public static async Task<string> Wget(this string url)
    {
      return await Wget(url, null).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a body from a URL.
    /// </summary>
    /// <param name="url">The url including query parameters.</param>
    /// <param name="logger">Optional logger for transport failures.</param>
    /// <returns>A response string, or <see cref="string.Empty"/> if any errors occurs.</returns>
    public static async Task<string> Wget(this string url, ILogger logger)
    {
      return await Wget(url, logger, DefaultRequestTimeout).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a body from a URL.
    /// </summary>
    /// <param name="url">The url including query parameters.</param>
    /// <param name="logger">Optional logger for transport failures.</param>
    /// <param name="timeout">The per-request timeout.</param>
    /// <returns>A response string, or <see cref="string.Empty"/> if any errors occurs.</returns>
    public static async Task<string> Wget(this string url, ILogger logger, TimeSpan timeout)
    {
      try
      {
        var result = await DoRequest(url, HttpMethod.Get, "application/json", null, true, logger, timeout)
            .ConfigureAwait(false);
        return result.Body ?? string.Empty;
      }
      catch (Exception ex)
      {
        logger?.LogError(ex, "HTTP request failed");
        return string.Empty;
      }
    }

    /// <summary>
    ///   Invokes a HTTP request to url.
    /// </summary>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <param name="noThrow">
    /// If it shall not throw exception. When this is set to true the exception is returned in the
    /// <see cref="RequestResponse"/>. If it is set to false, all exceptions are thrown. By default this parameter is
    /// true.
    /// </param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="ArgumentException">If <paramref name="method" /> is not GET, PUT, POST or DELETE.</exception>
    /// <exception cref="HttpRequestException">If any errors during the HTTP request.</exception>
    /// <remarks>
    /// If  <paramref name="noThrow"/> is set to true, the exception is passed in the <see cref="RequestResponse"/>
    /// otherwise it is thrown.
    /// </remarks>
    public static async Task<RequestResponse> DoRequest(this string url, HttpMethod method = null,
      string contentType = "application/json", string body = null, bool noThrow = true)
    {
      return await DoRequest(url, method, contentType, body, noThrow, null, DefaultRequestTimeout)
          .ConfigureAwait(false);
    }

    /// <summary>
    ///   Invokes a HTTP request to url.
    /// </summary>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <param name="noThrow">
    /// If it shall not throw exception. When this is set to true the exception is returned in the
    /// <see cref="RequestResponse"/> with status code 0. If it is set to false, all exceptions are thrown.
    /// </param>
    /// <param name="logger">Optional logger for transport failures.</param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="ArgumentException">If <paramref name="method" /> is not GET, PUT, POST or DELETE.</exception>
    /// <exception cref="HttpRequestException">If any errors during the HTTP request.</exception>
    public static async Task<RequestResponse> DoRequest(this string url, HttpMethod method,
      string contentType, string body, bool noThrow, ILogger logger)
    {
      return await DoRequest(url, method, contentType, body, noThrow, logger, DefaultRequestTimeout)
          .ConfigureAwait(false);
    }

    /// <summary>
    ///   Invokes a HTTP request to url.
    /// </summary>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <param name="noThrow">
    /// If it shall not throw exception. When this is set to true the exception is returned in the
    /// <see cref="RequestResponse"/> with status code 0. If it is set to false, all exceptions are thrown.
    /// </param>
    /// <param name="logger">Optional logger for transport failures.</param>
    /// <param name="timeout">The per-request timeout.</param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="ArgumentException">If <paramref name="method" /> is not GET, PUT, POST or DELETE.</exception>
    /// <exception cref="HttpRequestException">If any errors during the HTTP request.</exception>
    public static async Task<RequestResponse> DoRequest(this string url, HttpMethod method,
      string contentType, string body, bool noThrow, ILogger logger, TimeSpan timeout)
    {
      method ??= HttpMethod.Get;
      body ??= string.Empty;

      HttpResponseMessage response = null;
      using var requestCts = CreateTimeoutSource(timeout);
      try
      {
        if (method.Equals(HttpMethod.Get))
        {
          response = await Client.GetAsync(url, requestCts.Token).ConfigureAwait(false);
        }
        else if (method.Equals(HttpMethod.Post))
        {
          using var content = new StringContent(body);
          content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
          response = await Client.PostAsync(url, content, requestCts.Token).ConfigureAwait(false);
        }
        else if (method.Equals(HttpMethod.Put))
        {
          using var content = new StringContent(body);
          content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
          response = await Client.PutAsync(url, content, requestCts.Token).ConfigureAwait(false);
        }
        else if (method.Equals(HttpMethod.Delete))
        {
          response = await Client.DeleteAsync(url, requestCts.Token).ConfigureAwait(false);
        }
      }
      catch (Exception err)
      {
        if (noThrow)
        {
          logger?.LogError(err, "HTTP request failed");
          return new RequestResponse(null, (HttpStatusCode)0, null, err);
        }
        throw;
      }

      if (null == response)
        throw new ArgumentException(
          $"Unsupported HttpMethod specified '{method} - supported are GET, POST, PUT, DELETE", nameof(method));

      body = await response.Content.ReadAsStringAsync(requestCts.Token).ConfigureAwait(false);
      return new RequestResponse(response.Headers, response.StatusCode, body, null);
    }

    private static CancellationTokenSource CreateTimeoutSource(TimeSpan timeout)
    {
      return timeout == Timeout.InfiniteTimeSpan
          ? new CancellationTokenSource()
          : new CancellationTokenSource(timeout);
    }
  }
}
