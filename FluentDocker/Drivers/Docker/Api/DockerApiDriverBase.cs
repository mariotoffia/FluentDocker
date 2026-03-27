using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api
{
  /// <summary>
  /// Base class for Docker API driver components.
  /// Provides HTTP request/response helpers, JSON serialization, and error mapping.
  /// </summary>
  public abstract partial class DockerApiDriverBase
  {
    protected IDockerApiConnection Connection { get; private set; }
    protected DriverContext Context { get; private set; }

    protected DockerApiDriverBase(IDockerApiConnection connection)
    {
      ArgumentNullException.ThrowIfNull(connection);
      Connection = connection;
    }

    public virtual void Initialize(DriverContext context)
    {
      ArgumentNullException.ThrowIfNull(context);
      Context = context;
    }

    #region JSON Request/Response Helpers

    protected async Task<ApiResult<T>> GetJsonAsync<T>(string path, CancellationToken ct)
    {
      try
      {
        var response = await Connection.GetAsync(path, ct).ConfigureAwait(false);
        return await HandleResponseAsync<T>(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<T>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    /// <summary>
    /// Source-gen-aware GET that deserializes the response directly from the HTTP stream.
    /// </summary>
    protected async Task<ApiResult<T>> GetJsonAsync<T>(
        string path, JsonTypeInfo<T> responseTypeInfo, CancellationToken ct)
    {
      try
      {
        var response = await Connection.GetAsync(path, ct).ConfigureAwait(false);
        return await HandleResponseFromStreamAsync(response, responseTypeInfo, ct)
            .ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<T>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<ApiResult<T>> PostJsonAsync<T>(
        string path, object body, CancellationToken ct)
    {
      try
      {
        var content = body != null
            ? new StringContent(
                JsonHelper.Serialize(body), Encoding.UTF8, "application/json")
            : null;

        var response = await Connection.PostAsync(path, content, ct).ConfigureAwait(false);
        return await HandleResponseAsync<T>(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<T>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    /// <summary>
    /// Source-gen-aware POST that serializes the body and deserializes the response
    /// directly from the HTTP stream, skipping intermediate string allocations.
    /// </summary>
    protected async Task<ApiResult<TResponse>> PostJsonAsync<TBody, TResponse>(
        string path, TBody body,
        JsonTypeInfo<TBody> bodyTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
      try
      {
        var content = body != null
            ? JsonContent.Create(body, bodyTypeInfo)
            : null;

        var response = await Connection.PostAsync(path, content, ct).ConfigureAwait(false);
        return await HandleResponseFromStreamAsync(response, responseTypeInfo, ct)
            .ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<TResponse>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    /// <summary>
    /// Source-gen-aware POST with no body, deserializes the response directly from stream.
    /// </summary>
    protected async Task<ApiResult<T>> PostJsonAsync<T>(
        string path, JsonTypeInfo<T> responseTypeInfo, CancellationToken ct)
    {
      try
      {
        var response = await Connection.PostAsync(path, null, ct).ConfigureAwait(false);
        return await HandleResponseFromStreamAsync(response, responseTypeInfo, ct)
            .ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<T>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<ApiResult> PostAsync(string path, object body, CancellationToken ct)
    {
      try
      {
        var content = body != null
            ? new StringContent(
                JsonHelper.Serialize(body), Encoding.UTF8, "application/json")
            : null;

        var response = await Connection.PostAsync(path, content, ct).ConfigureAwait(false);
        return await HandleResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<ApiResult> DeleteAsync(string path, CancellationToken ct)
    {
      try
      {
        var response = await Connection.DeleteAsync(path, ct).ConfigureAwait(false);
        return await HandleResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<ApiResult> PutStreamAsync(
        string path, Stream stream, string contentType, CancellationToken ct)
    {
      try
      {
        var content = new StreamContent(stream);
        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await Connection.PutAsync(path, content, ct).ConfigureAwait(false);
        return await HandleResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<ApiResult<JsonElement>> GetJsonElementAsync(
        string path, CancellationToken ct)
    {
      try
      {
        var response = await Connection.GetAsync(path, ct).ConfigureAwait(false);
        return await HandleJsonElementResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<JsonElement>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<ApiResult<JsonElement>> PostJsonElementAsync(
        string path, object body, CancellationToken ct)
    {
      try
      {
        var content = body != null
            ? new StringContent(
                JsonHelper.Serialize(body), Encoding.UTF8, "application/json")
            : null;

        var response = await Connection.PostAsync(path, content, ct).ConfigureAwait(false);
        return await HandleJsonElementResponseAsync(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult<JsonElement>.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<Stream> GetRawStreamAsync(string path, CancellationToken ct)
    {
      return await Connection.GetStreamAsync(path, ct).ConfigureAwait(false);
    }

    protected async IAsyncEnumerable<string> ReadNdjsonStreamAsync(
        string path, [EnumeratorCancellation] CancellationToken ct)
    {
      Stream stream;
      try
      {
        stream = await Connection.GetStreamAsync(path, ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.Log($"NDJSON stream open failed: {ex.Message}");
        yield break;
      }

      using var reader = new StreamReader(stream, Encoding.UTF8);
      while (!ct.IsCancellationRequested)
      {
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          yield break;
        }
        catch (Exception ex)
        {
          Logger.Log($"NDJSON stream read failed: {ex.Message}");
          yield break;
        }

        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;
        yield return line;
      }
    }

    protected async IAsyncEnumerable<string> ReadNdjsonFromPostStreamAsync(
        string path, HttpContent content, [EnumeratorCancellation] CancellationToken ct)
    {
      Stream stream;
      try
      {
        stream = await Connection.PostStreamAsync(path, content, ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.Log($"NDJSON POST stream open failed: {ex.Message}");
        yield break;
      }

      using var reader = new StreamReader(stream, Encoding.UTF8);
      while (!ct.IsCancellationRequested)
      {
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          yield break;
        }
        catch (Exception ex)
        {
          Logger.Log($"NDJSON POST stream read failed: {ex.Message}");
          yield break;
        }

        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;
        yield return line;
      }
    }

    /// <summary>
    /// Source-gen-aware GET NDJSON stream: deserializes each line from UTF-8 bytes
    /// via <see cref="System.IO.Pipelines.PipeReader"/>, skipping per-line string allocation.
    /// </summary>
    protected async IAsyncEnumerable<T> ReadNdjsonStreamAsync<T>(
        string path, JsonTypeInfo<T> typeInfo,
        [EnumeratorCancellation] CancellationToken ct) where T : class
    {
      Stream stream;
      try
      {
        stream = await Connection.GetStreamAsync(path, ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.Log($"NDJSON stream open failed: {ex.Message}");
        yield break;
      }

      await foreach (var item in ReadNdjsonLinesAsync(stream, typeInfo, ct)
          .ConfigureAwait(false))
        yield return item;
    }

    /// <summary>
    /// Source-gen-aware POST NDJSON stream: deserializes each line from UTF-8 bytes
    /// via <see cref="System.IO.Pipelines.PipeReader"/>.
    /// </summary>
    protected async IAsyncEnumerable<T> ReadNdjsonFromPostStreamAsync<T>(
        string path, HttpContent content, JsonTypeInfo<T> typeInfo,
        [EnumeratorCancellation] CancellationToken ct) where T : class
    {
      Stream stream;
      try
      {
        stream = await Connection.PostStreamAsync(path, content, ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.Log($"NDJSON POST stream open failed: {ex.Message}");
        yield break;
      }

      await foreach (var item in ReadNdjsonLinesAsync(stream, typeInfo, ct)
          .ConfigureAwait(false))
        yield return item;
    }

    #endregion

    #region Response Handling

    private static async Task<ApiResult<T>> HandleResponseAsync<T>(
        HttpResponseMessage response, CancellationToken ct)
    {
      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

      if (response.IsSuccessStatusCode)
      {
        if (string.IsNullOrWhiteSpace(body))
          return ApiResult<T>.Ok(default);

        var data = JsonSerializer.Deserialize<T>(body, JsonHelper.CaseInsensitiveOptions);
        return ApiResult<T>.Ok(data);
      }

      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<T>.Failure((int)response.StatusCode, errorMessage, body);
    }

    /// <summary>
    /// Stream-based response handler that deserializes directly from the HTTP stream
    /// using source-generated <see cref="JsonTypeInfo{T}"/>, skipping the intermediate string.
    /// Error paths still read as string for error message extraction.
    /// </summary>
    private static async Task<ApiResult<T>> HandleResponseFromStreamAsync<T>(
        HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
      if (response.IsSuccessStatusCode)
      {
        if (response.Content.Headers.ContentLength == 0)
          return ApiResult<T>.Ok(default);

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var data = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct)
            .ConfigureAwait(false);
        return ApiResult<T>.Ok(data);
      }

      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<T>.Failure((int)response.StatusCode, errorMessage, body);
    }

    /// <summary>
    /// Stream-based response handler for <see cref="JsonElement"/> results.
    /// Parses the JSON document directly from the HTTP stream.
    /// </summary>
    private static async Task<ApiResult<JsonElement>> HandleJsonElementResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      if (response.IsSuccessStatusCode)
      {
        if (response.Content.Headers.ContentLength == 0)
          return ApiResult<JsonElement>.Ok(default);

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
            .ConfigureAwait(false);
        return ApiResult<JsonElement>.Ok(doc.RootElement.Clone());
      }

      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<JsonElement>.Failure((int)response.StatusCode, errorMessage, body);
    }

    private static async Task<ApiResult> HandleResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
        return ApiResult.Ok();

      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult.Failure((int)response.StatusCode, errorMessage, body);
    }

    private static string ExtractErrorMessage(string body)
    {
      if (string.IsNullOrWhiteSpace(body))
        return null;
      try
      {
        var error = JsonHelper.TryDeserialize<DockerApiErrorResponse>(body);
        return error?.Message;
      }
      catch (Exception ex)
      {
        Logger.Log($"Error response JSON parsing failed: {ex.Message}");
        return body.Length > 500 ? body[..500] : body;
      }
    }

    #endregion

    #region Error Context

    protected ErrorContext CreateErrorContext(
        string operation, int statusCode, string responseBody = null)
    {
      return new ErrorContext(operation)
      {
        DriverId = Context?.DriverId,
        Host = Context?.Host,
        ExitCode = statusCode,
        StdOut = responseBody,
        Metadata = { ["HttpStatusCode"] = statusCode.ToString() }
      };
    }

    protected static string MapNotFoundErrorCode(int statusCode, string defaultErrorCode)
    {
      return statusCode == 404 ? defaultErrorCode : MapHttpErrorCode(statusCode);
    }

    protected static string MapHttpErrorCode(int statusCode)
    {
      return statusCode switch
      {
        400 => ErrorCodes.Api.BadRequest,
        401 => ErrorCodes.Api.Unauthorized,
        404 => ErrorCodes.Api.NotFound,
        409 => ErrorCodes.Api.Conflict,
        >= 500 => ErrorCodes.Api.ServerError,
        _ => ErrorCodes.Api.BadRequest
      };
    }

    #endregion

    private static bool IsConnectionError(Exception ex) =>
        ex is HttpRequestException or System.Net.Sockets.SocketException or TaskCanceledException;
  }
}
