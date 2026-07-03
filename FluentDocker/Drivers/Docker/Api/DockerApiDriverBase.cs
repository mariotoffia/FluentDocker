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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>
    /// Logger for this driver component. Category equals the concrete derived type's FQN.
    /// </summary>
    protected ILogger Logger { get; private set; } = NullLogger.Instance;

    protected DockerApiDriverBase(IDockerApiConnection connection)
    {
      ArgumentNullException.ThrowIfNull(connection);
      Connection = connection;
    }

    public virtual void Initialize(DriverContext context)
    {
      ArgumentNullException.ThrowIfNull(context);
      Context = context;
      Logger = context.LoggerFactory.CreateLogger(GetType());
    }

    #region JSON Request/Response Helpers

    protected async Task<ApiResult<T>> GetJsonAsync<T>(string path, CancellationToken ct)
    {
      try
      {
        var response = await Connection.GetAsync(path, ct).ConfigureAwait(false);
        return await HandleResponseAsync<T>(response, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (Exception ex) when (IsConnectionError(ex, ct))
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
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "NDJSON stream open failed");
        throw new DriverException(
            $"Failed to open NDJSON stream for '{path}': {ex.Message}",
            ErrorCodes.Api.ServerError, ex);
      }

      // The await using owns stream cleanup; StreamReader is leaveOpen:true.
      await using var _ = stream.ConfigureAwait(false);
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
          bufferSize: 1024, leaveOpen: true);
      while (true)
      {
        ct.ThrowIfCancellationRequested();
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          Logger.LogDebug(ex, "NDJSON stream read failed");
          throw new DriverException(
              $"Failed to read NDJSON stream for '{path}': {ex.Message}",
              ErrorCodes.Api.ServerError, ex);
        }

        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;
        yield return line;
        ct.ThrowIfCancellationRequested();
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
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "NDJSON POST stream open failed");
        throw new DriverException(
            $"Failed to open NDJSON POST stream for '{path}': {ex.Message}",
            ErrorCodes.Api.ServerError, ex);
      }

      await using var _ = stream.ConfigureAwait(false);
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
          bufferSize: 1024, leaveOpen: true);
      while (true)
      {
        ct.ThrowIfCancellationRequested();
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          Logger.LogDebug(ex, "NDJSON POST stream read failed");
          throw new DriverException(
              $"Failed to read NDJSON POST stream for '{path}': {ex.Message}",
              ErrorCodes.Api.ServerError, ex);
        }

        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;
        yield return line;
        ct.ThrowIfCancellationRequested();
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
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "NDJSON stream open failed");
        throw new DriverException(
            $"Failed to open NDJSON stream for '{path}': {ex.Message}",
            ErrorCodes.Api.ServerError, ex);
      }

      await using var _ = stream.ConfigureAwait(false);
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
      await foreach (var item in ReadNdjsonFromPostStreamAsync(
          path, content, null, typeInfo, ct).ConfigureAwait(false))
        yield return item;
    }

    protected async IAsyncEnumerable<T> ReadNdjsonFromPostStreamAsync<T>(
        string path, HttpContent content, IReadOnlyDictionary<string, string> headers,
        JsonTypeInfo<T> typeInfo, [EnumeratorCancellation] CancellationToken ct) where T : class
    {
      Stream stream;
      try
      {
        stream = await Connection.PostStreamAsync(path, content, headers, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "NDJSON POST stream open failed");
        throw new DriverException(
            $"Failed to open NDJSON POST stream for '{path}': {ex.Message}",
            ErrorCodes.Api.ServerError, ex);
      }

      await using var __ = stream.ConfigureAwait(false);
      await foreach (var item in ReadNdjsonLinesAsync(stream, typeInfo, ct)
          .ConfigureAwait(false))
        yield return item;
    }

    #endregion
  }
}
