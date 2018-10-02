using System;
using System.Net;
using System.Net.Http.Headers;

namespace Ductus.FluentDocker.Common
{
  public struct RequestResponse
  {
    internal RequestResponse(HttpResponseHeaders headers, HttpStatusCode code, string body, Exception err)
    {
      Headers = headers;
      Code = code;
      Body = body;
      Err = err;
    }

    public HttpResponseHeaders Headers { get; }
    public HttpStatusCode Code { get; }
    public string Body { get; }
    public Exception Err { get; }
  }
}