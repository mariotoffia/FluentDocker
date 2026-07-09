using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Api
{
  public abstract partial class DockerApiDriverBase
  {
    /// <summary>Sends a POST with custom headers and maps the response to an API result.</summary>
    protected async Task<ApiResult> PostAsync(
        string path, object body, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
      try
      {
        var content = body != null
            ? new StringContent(JsonHelper.Serialize(body), Encoding.UTF8, "application/json")
            : null;
        var response = await Connection.PostAsync(path, content, headers, ct).ConfigureAwait(false);
        return await HandleResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex, ct))
      {
        return TransportFailure(ex);
      }
    }

    /// <summary>Sends a POST with custom headers and returns a JSON element response.</summary>
    protected async Task<ApiResult<System.Text.Json.JsonElement>> PostJsonElementAsync(
        string path, object body, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
      try
      {
        var content = body != null
            ? new StringContent(JsonHelper.Serialize(body), Encoding.UTF8, "application/json")
            : null;
        var response = await Connection.PostAsync(path, content, headers, ct).ConfigureAwait(false);
        return await HandleJsonElementResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex, ct))
      {
        return TransportFailure<System.Text.Json.JsonElement>(ex);
      }
    }
  }
}
