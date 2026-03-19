using System;
using System.Net;
using System.Net.Http.Headers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Represents the response from an HTTP request to the Docker/Podman API.
  /// </summary>
  public struct RequestResponse
  {
    internal RequestResponse(HttpResponseHeaders headers, HttpStatusCode code, string body, Exception err)
    {
      Headers = headers;
      Code = code;
      Body = body;
      Err = err;
    }

    /// <summary>The HTTP response headers.</summary>
    public HttpResponseHeaders Headers { get; }

    /// <summary>The HTTP status code returned by the API.</summary>
    public HttpStatusCode Code { get; }

    /// <summary>The response body as a string.</summary>
    public string Body { get; }

    /// <summary>The exception that occurred during the request, or null on success.</summary>
    public Exception Err { get; }
  }
}
