using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;

// ReSharper disable IdentifierTypo

namespace Ductus.FluentDocker.Extensions
{
  public static class HttpExtensions
  {
    private static readonly HttpClient Client = new HttpClient();

    /// <summary>
    /// Downloads a resource to local path and filename.
    /// </summary>
    /// <param name="url">The url to resource to download.</param>
    /// <param name="fqPath">The fully qualified path to the file where the data is stored.</param>
    /// <returns></returns>
    public static async Task<bool> Download(this Uri url, string fqPath)
    {
      var response = await Client.GetAsync(url);

      if (response.IsSuccessStatusCode)
      {
        using (var fs = new FileStream(fqPath, FileMode.Create))
        {
          await response.Content.CopyToAsync(fs);
          fs.Flush();
        }
      }
      else
      {
        throw new FluentDockerException(
          $"Could not download file ${url} code: ${response.StatusCode}"
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
      try
      {
        var result = await DoRequest(url);
        return result.Body ?? string.Empty;
      }
      catch
      {
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
      if (null == method)
        method = HttpMethod.Get;
      if (null == body)
        body = string.Empty;

      HttpContent content = new StringContent(body);
      content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

      HttpResponseMessage response = null;
      try
      {
        if (method.Equals(HttpMethod.Get))
          response = await Client.GetAsync(url);
        if (method.Equals(HttpMethod.Post))
          response = await Client.PostAsync(url, content);
        if (method.Equals(HttpMethod.Put))
          response = await Client.PutAsync(url, content);
        if (method.Equals(HttpMethod.Delete))
          response = await Client.DeleteAsync(url);

      }
      catch (Exception err)
      {
        if (noThrow)
        {
          return new RequestResponse(null, HttpStatusCode.InternalServerError, body, err);
        }
        throw;
      }

      if (null == response)
        throw new ArgumentException(
          $"Unsupported HttpMethod specified '{method} - supported are GET, POST, PUT, DELETE", nameof(method));

      body = await response.Content.ReadAsStringAsync();
      return new RequestResponse(response.Headers, response.StatusCode, body, null);
    }
  }
}
