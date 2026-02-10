using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Api
{
  /// <summary>
  /// Base class for Docker API driver components.
  /// Provides HTTP request/response helpers, JSON serialization, and error mapping.
  /// </summary>
  public abstract class DockerApiDriverBase
  {
    protected IDockerApiConnection Connection { get; private set; }
    protected DriverContext Context { get; private set; }

    protected DockerApiDriverBase(IDockerApiConnection connection) => Connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public virtual void Initialize(DriverContext context)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    #region JSON Request/Response Helpers

    protected async Task<ApiResult<T>> GetJsonAsync<T>(string path, CancellationToken ct)
    {
      try
      {
        var response = await Connection.GetAsync(path, ct);
        return await HandleResponseAsync<T>(response, ct);
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
                JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            : null;

        var response = await Connection.PostAsync(path, content, ct);
        return await HandleResponseAsync<T>(response, ct);
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
                JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            : null;

        var response = await Connection.PostAsync(path, content, ct);
        return await HandleResponseAsync(response, ct);
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
        var response = await Connection.DeleteAsync(path, ct);
        return await HandleResponseAsync(response, ct);
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
        var response = await Connection.PutAsync(path, content, ct);
        return await HandleResponseAsync(response, ct);
      }
      catch (Exception ex) when (IsConnectionError(ex))
      {
        return ApiResult.Failure((int)HttpStatusCode.ServiceUnavailable,
            $"Cannot connect to Docker daemon: {ex.Message}");
      }
    }

    protected async Task<Stream> GetRawStreamAsync(string path, CancellationToken ct)
    {
      return await Connection.GetStreamAsync(path, ct);
    }

    protected async IAsyncEnumerable<string> ReadNdjsonStreamAsync(
        string path, [EnumeratorCancellation] CancellationToken ct)
    {
      Stream stream;
      try
      {
        stream = await Connection.GetStreamAsync(path, ct);
      }
      catch
      {
        yield break;
      }

      using var reader = new StreamReader(stream, Encoding.UTF8);
      while (!ct.IsCancellationRequested)
      {
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct);
        }
        catch (OperationCanceledException)
        {
          yield break;
        }
        catch
        {
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
        stream = await Connection.PostStreamAsync(path, content, ct);
      }
      catch
      {
        yield break;
      }

      using var reader = new StreamReader(stream, Encoding.UTF8);
      while (!ct.IsCancellationRequested)
      {
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct);
        }
        catch (OperationCanceledException)
        {
          yield break;
        }
        catch
        {
          yield break;
        }

        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;
        yield return line;
      }
    }

    #endregion

    #region Response Handling

    private async Task<ApiResult<T>> HandleResponseAsync<T>(
        HttpResponseMessage response, CancellationToken ct)
    {
      var body = await response.Content.ReadAsStringAsync(ct);

      if (response.IsSuccessStatusCode)
      {
        if (string.IsNullOrWhiteSpace(body))
          return ApiResult<T>.Ok(default);

        var data = JsonConvert.DeserializeObject<T>(body);
        return ApiResult<T>.Ok(data);
      }

      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<T>.Failure((int)response.StatusCode, errorMessage, body);
    }

    private async Task<ApiResult> HandleResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
        return ApiResult.Ok();

      var body = await response.Content.ReadAsStringAsync(ct);
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
        var error = JsonConvert.DeserializeObject<DockerApiErrorResponse>(body);
        return error?.Message;
      }
      catch
      {
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

    protected string MapNotFoundErrorCode(int statusCode, string defaultErrorCode)
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

    private static bool IsConnectionError(Exception ex)
    {
      return ex is HttpRequestException or System.Net.Sockets.SocketException or TaskCanceledException;
    }

    #region Stream Helpers

    /// <summary>
    /// Strips Docker multiplexed stream 8-byte header frames from raw log output.
    /// Frame format: [1B stream type][3B padding][4B big-endian size][payload].
    /// </summary>
    protected static string StripDockerStreamHeaders(string raw)
    {
      if (string.IsNullOrEmpty(raw))
        return string.Empty;

      var bytes = Encoding.UTF8.GetBytes(raw);
      if (bytes.Length < 8)
        return raw;

      // Check if first byte is a valid Docker stream header (0=stdin, 1=stdout, 2=stderr)
      if (bytes[0] > 2 || bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0)
        return raw;

      var sb = new StringBuilder();
      var offset = 0;
      while (offset + 8 <= bytes.Length)
      {
        var frameSize = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16)
                      | (bytes[offset + 6] << 8) | bytes[offset + 7];
        offset += 8;
        if (frameSize <= 0 || offset + frameSize > bytes.Length)
          break;
        sb.Append(Encoding.UTF8.GetString(bytes, offset, frameSize));
        offset += frameSize;
      }
      return sb.Length > 0 ? sb.ToString() : raw;
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/>,
    /// handling partial reads. Returns the total number of bytes actually read.
    /// </summary>
    protected static async Task<int> ReadExactAsync(
        Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
      var totalRead = 0;
      while (totalRead < count)
      {
        var read = await stream.ReadAsync(
            buffer.AsMemory(totalRead, count - totalRead), ct);
        if (read == 0)
          break;
        totalRead += read;
      }
      return totalRead;
    }

    #endregion
  }

  #region Internal Result Types

  public class ApiResult<T>
  {
    public bool Success { get; private init; }
    public T Data { get; private init; }
    public int StatusCode { get; private init; }
    public string ErrorMessage { get; private init; }
    public string ResponseBody { get; private init; }

    public static ApiResult<T> Ok(T data) =>
        new() { Success = true, Data = data, StatusCode = 200 };

    public static ApiResult<T> Failure(int statusCode, string error, string body = null) =>
        new() { Success = false, StatusCode = statusCode, ErrorMessage = error, ResponseBody = body };
  }

  public class ApiResult
  {
    public bool Success { get; private init; }
    public int StatusCode { get; private init; }
    public string ErrorMessage { get; private init; }
    public string ResponseBody { get; private init; }

    public static ApiResult Ok() =>
        new() { Success = true, StatusCode = 200 };

    public static ApiResult Failure(int statusCode, string error, string body = null) =>
        new() { Success = false, StatusCode = statusCode, ErrorMessage = error, ResponseBody = body };
  }

  #endregion
}
