using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Drivers.Models
{
  /// <summary>
  /// HTTP/API adapter for the OpenAI-compatible inference data plane. Speaks raw
  /// OpenAI JSON over an <see cref="IModelApiConnection"/>; it does NOT extend the
  /// Docker API driver base (which is bound to the Docker socket + envelope).
  /// Streaming methods live in the <c>.Streaming.cs</c> partial.
  /// </summary>
  public partial class OpenAiModelInferenceDriver : IModelInferenceDriver
  {
    private readonly IModelApiConnection _connection;
    private readonly ModelRunnerEndpoint _endpoint;

    /// <summary>Initializes the driver.</summary>
    /// <param name="connection">The HTTP connection.</param>
    /// <param name="endpoint">The resolved endpoint (engine path source).</param>
    public OpenAiModelInferenceDriver(IModelApiConnection connection, ModelRunnerEndpoint endpoint)
    {
      _connection = connection ?? throw new ArgumentNullException(nameof(connection));
      _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ChatCompletionResponse>> ChatCompletionAsync(
        DriverContext context, ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      // Copy so we never mutate the caller's instance (Stream is forced off here).
      var req = new ChatCompletionRequest(request) { Stream = false };
      return await PostJsonAsync<ChatCompletionRequest, ChatCompletionResponse>(
          context, "/chat/completions", req, "ChatCompletion", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<CompletionResponse>> CompletionAsync(
        DriverContext context, CompletionRequest request, CancellationToken cancellationToken = default)
    {
      // Copy so we never mutate the caller's instance (Stream is forced off here).
      var req = new CompletionRequest(request) { Stream = false };
      return await PostJsonAsync<CompletionRequest, CompletionResponse>(
          context, "/completions", req, "Completion", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<EmbeddingsResponse>> EmbeddingsAsync(
        DriverContext context, EmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
      // Copy so the wire body cannot be affected by post-call mutation of the caller's instance.
      var req = new EmbeddingsRequest(request);
      return await PostJsonAsync<EmbeddingsRequest, EmbeddingsResponse>(
          context, "/embeddings", req, "Embeddings", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<OpenAiModel>>> ListEngineModelsAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        using var response = await _connection.GetAsync(Path("/models"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
          var error = await SafeReadError(response, cancellationToken).ConfigureAwait(false);
          return CommandResponse<IList<OpenAiModel>>.Fail(
              FormatHttpError("ListEngineModels", "/models", null, response.StatusCode, error, modelMissingEligible: false),
              ErrorCodeFor(response.StatusCode, error, modelMissingEligible: false),
              CreateApiErrorContext(context, "ListEngineModels", response),
              (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync<OpenAiModelList>(stream, JsonHelper.CaseInsensitiveOptions, cancellationToken).ConfigureAwait(false);
        return CommandResponse<IList<OpenAiModel>>.Ok(list?.Data ?? []);
      }
      catch (ModelRunnerException ex)
      {
        // A transport-level failure (refused connection, socket error) surfaces from the
        // connection as ModelRunnerException(EndpointUnreachable). Preserve its code + context
        // instead of letting the broad catch below downgrade it to RequestFailed.
        return CommandResponse<IList<OpenAiModel>>.Fail(ex.Message, ex.ErrorCode, ex.Context);
      }
      catch (JsonException ex)
      {
        // A malformed /models listing is a parse failure, not a transport RequestFailed.
        return CommandResponse<IList<OpenAiModel>>.Fail(
            $"ListEngineModels: malformed inference response JSON: {ex.Message}",
            ErrorCodes.ModelInference.StreamParseError,
            new ErrorContext("ListEngineModels") { DriverId = context?.DriverId, Host = context?.Host });
      }
      catch (TimeoutException ex)
      {
        // A per-request timeout is distinct from both a caller cancel (rethrown via the broad
        // catch's OperationCanceledException guard) and a generic server RequestFailed.
        return CommandResponse<IList<OpenAiModel>>.Fail(ex.Message, ErrorCodes.ModelInference.Timeout);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<IList<OpenAiModel>>.Fail(ex.Message, ErrorCodes.ModelInference.RequestFailed);
      }
    }

    private async Task<CommandResponse<TResponse>> PostJsonAsync<TRequest, TResponse>(
        DriverContext context, string suffix, TRequest request, string operation, CancellationToken cancellationToken)
    {
      try
      {
        var json = JsonSerializer.Serialize(request, JsonHelper.DefaultOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _connection.PostAsync(Path(suffix), content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
          var error = await SafeReadError(response, cancellationToken).ConfigureAwait(false);
          return CommandResponse<TResponse>.Fail(
              FormatHttpError(operation, suffix, request, response.StatusCode, error),
              ErrorCodeFor(response.StatusCode, error),
              CreateApiErrorContext(context, operation, response),
              (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // A literal JSON `null` body (or empty stream) deserializes to null without throwing.
        // Treat it as a protocol/parse failure rather than a successful null payload — a null
        // response is never a valid OpenAI completion/embeddings result. Return a failed
        // CommandResponse rather than throwing so callers always get a typed result.
        var dto = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonHelper.CaseInsensitiveOptions, cancellationToken).ConfigureAwait(false);
        if (dto is null)
          return CommandResponse<TResponse>.Fail(
              $"{operation}: inference response body was null (expected JSON object)",
              ErrorCodes.ModelInference.StreamParseError,
              CreateApiErrorContext(context, operation, response));

        return CommandResponse<TResponse>.Ok(dto);
      }
      catch (OperationCanceledException) { throw; }
      catch (TimeoutException ex)
      {
        // A per-request idle/timeout from the connection (SendWithTimeoutAsync throws
        // TimeoutException) is NOT a generic server failure — surface it as a distinct Timeout so
        // callers can retry/backoff on latency rather than treating it like a 500. This is checked
        // BEFORE the broad catch (which would otherwise collapse it to RequestFailed), and AFTER
        // the OperationCanceledException rethrow so a caller cancel is never reported as a timeout.
        return CommandResponse<TResponse>.Fail(ex.Message, ErrorCodes.ModelInference.Timeout);
      }
      catch (ModelRunnerException ex)
      {
        // Preserve the typed transport error (e.g. EndpointUnreachable) + its context rather
        // than collapsing it to RequestFailed in the broad catch below.
        return CommandResponse<TResponse>.Fail(ex.Message, ex.ErrorCode, ex.Context);
      }
      catch (JsonException ex)
      {
        // A malformed / non-JSON response body is a protocol/parse failure, NOT a transport
        // RequestFailed — surface it distinctly (StreamParseError) with the offending context
        // so callers can tell "the server replied with garbage" from "the call never landed".
        return CommandResponse<TResponse>.Fail(
            $"{operation}: malformed inference response JSON: {ex.Message}",
            ErrorCodes.ModelInference.StreamParseError,
            new ErrorContext(operation) { DriverId = context?.DriverId, Host = context?.Host });
      }
      catch (Exception ex)
      {
        return CommandResponse<TResponse>.Fail(ex.Message, ErrorCodes.ModelInference.RequestFailed);
      }
    }

    private string Path(string suffix) => _endpoint.EngineV1Path(suffix);

    // Accepts a nullable status so the streaming path can pass HttpRequestException.StatusCode
    // directly — a connect failure (no response, null status) maps to RequestFailed, matching
    // the non-streaming catch.
    private static string ErrorCodeFor(HttpStatusCode? code, string error = null, bool modelMissingEligible = true) => code switch
    {
      HttpStatusCode.Unauthorized => ErrorCodes.ModelInference.Unauthorized,
      HttpStatusCode.NotFound when modelMissingEligible && LooksLikeModelMissing(error) => ErrorCodes.ModelInference.ModelNotLoaded,
      HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => ErrorCodes.ModelInference.ServiceUnavailable,
      _ => ErrorCodes.ModelInference.RequestFailed
    };

    private static string FormatHttpError(
        string operation, string suffix, object request, HttpStatusCode status, string error,
        bool modelMissingEligible = true)
    {
      if (status != HttpStatusCode.NotFound)
        return error;

      var route = suffix;
      if (modelMissingEligible && LooksLikeModelMissing(error))
      {
        var model = ModelIdFor(request);
        var modelText = string.IsNullOrEmpty(model) ? "the requested model" : $"model '{model}'";
        return $"{operation}: {modelText} is not loaded or not found. Pull/load it; if it exists, verify the endpoint/base path. HTTP 404: {error}";
      }

      return $"{operation}: endpoint/base path route '{route}' was not found. Verify the endpoint URL, raw base path, and DMR engine path. HTTP 404: {error}";
    }

    /// <summary>
    /// DMR-version-sensitive 404-body heuristic: current DMR builds report missing/unloaded
    /// models as text containing "model" plus "not found" or "not loaded".
    /// </summary>
    private static bool LooksLikeModelMissing(string error) =>
        !string.IsNullOrEmpty(error) &&
        error.Contains("model", StringComparison.OrdinalIgnoreCase) &&
        (error.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("not loaded", StringComparison.OrdinalIgnoreCase));

    private static string ModelIdFor(object request) => request switch
    {
      ChatCompletionRequest r => r.Model,
      CompletionRequest r => r.Model,
      EmbeddingsRequest r => r.Model,
      _ => null
    };

    private static async Task<string> SafeReadError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
      try
      {
        // Read AT MOST 64 KiB from the (potentially large / hostile) error body so a multi-MiB
        // response can never force unbounded materialization. Decode (UTF-8) then truncate to 512.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[65536];
        var total = 0;
        while (total < buffer.Length)
        {
          var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
          if (read == 0)
            break;
          total += read;
        }

        var body = Encoding.UTF8.GetString(buffer, 0, total);
        return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : Truncate(body, 512);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // Caller cancellation must propagate, not be masked as a generic HTTP error.
        throw;
      }
      catch (Exception)
      {
        return $"HTTP {(int)response.StatusCode}";
      }
    }

    private static ErrorContext CreateApiErrorContext(DriverContext context, string operation, HttpResponseMessage response) =>
        new(operation)
        {
          DriverId = context?.DriverId,
          Host = context?.Host,
          ExitCode = (int)response.StatusCode
        };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
  }
}
