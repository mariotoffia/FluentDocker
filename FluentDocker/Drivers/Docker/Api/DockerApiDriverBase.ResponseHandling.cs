using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.ApiModels;

namespace FluentDocker.Drivers.Docker.Api
{
  public abstract partial class DockerApiDriverBase
  {
    #region Response Handling

    private static async Task<ApiResult<T>> HandleResponseAsync<T>(
        HttpResponseMessage response, CancellationToken ct)
    {
      using var responseToDispose = response;
      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

      if (response.IsSuccessStatusCode)
      {
        if (string.IsNullOrWhiteSpace(body))
          return ApiResult<T>.Failure((int)response.StatusCode,
              "Docker API returned an empty response body where JSON was expected", body);

        try
        {
          var data = JsonSerializer.Deserialize<T>(body, JsonHelper.CaseInsensitiveOptions);
          return ApiResult<T>.Ok(data!, (int)response.StatusCode);
        }
        catch (JsonException ex)
        {
          return ApiResult<T>.Failure((int)response.StatusCode,
              $"Failed to parse Docker API response: {ex.Message}", body);
        }
      }

      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<T>.Failure((int)response.StatusCode, errorMessage, body);
    }

    /// <summary>
    /// Response handler that deserializes from the buffered content stream using
    /// source-generated <see cref="JsonTypeInfo{T}"/>, skipping the intermediate string.
    /// Error paths still read as string for error message extraction.
    /// </summary>
    private static async Task<ApiResult<T>> HandleResponseFromStreamAsync<T>(
        HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
      using var responseToDispose = response;
      if (response.IsSuccessStatusCode)
      {
        if (response.Content.Headers.ContentLength == 0)
          return ApiResult<T>.Failure((int)response.StatusCode,
              "Docker API returned an empty response body where JSON was expected");

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try
        {
          var data = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct)
              .ConfigureAwait(false);
          return ApiResult<T>.Ok(data!, (int)response.StatusCode);
        }
        catch (JsonException ex)
        {
          return ApiResult<T>.Failure((int)response.StatusCode,
              $"Failed to parse Docker API response: {ex.Message}");
        }
        catch (IOException ex)
        {
          return ApiResult<T>.Failure((int)response.StatusCode,
              $"Failed to parse Docker API response: {ex.Message}");
        }
      }

      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<T>.Failure((int)response.StatusCode, errorMessage, body);
    }

    /// <summary>
    /// Response handler for <see cref="JsonElement"/> results.
    /// Parses the JSON document from the buffered content stream.
    /// </summary>
    private static async Task<ApiResult<JsonElement>> HandleJsonElementResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      using var responseToDispose = response;
      if (response.IsSuccessStatusCode)
      {
        if (response.Content.Headers.ContentLength == 0)
          return ApiResult<JsonElement>.Failure((int)response.StatusCode,
              "Docker API returned an empty response body where JSON was expected");

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try
        {
          using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
              .ConfigureAwait(false);
          return ApiResult<JsonElement>.Ok(doc.RootElement.Clone(), (int)response.StatusCode);
        }
        catch (JsonException ex)
        {
          return ApiResult<JsonElement>.Failure((int)response.StatusCode,
              $"Failed to parse Docker API response: {ex.Message}");
        }
        catch (IOException ex)
        {
          return ApiResult<JsonElement>.Failure((int)response.StatusCode,
              $"Failed to parse Docker API response: {ex.Message}");
        }
      }

      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult<JsonElement>.Failure((int)response.StatusCode, errorMessage, body);
    }

    private static async Task<ApiResult> HandleResponseAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      using var responseToDispose = response;
      if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
        return ApiResult.Ok((int)response.StatusCode);

      var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
      var errorMessage = ExtractErrorMessage(body) ??
          $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
      return ApiResult.Failure((int)response.StatusCode, errorMessage, body);
    }

    private static string? ExtractErrorMessage(string body)
    {
      if (string.IsNullOrWhiteSpace(body))
        return null;

      return JsonHelper.TryDeserialize<DockerApiErrorResponse>(body, out var error)
          ? error?.Message
          : null;
    }

    #endregion
  }
}
