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

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// HTTP/API adapter for the OpenAI-compatible inference data plane. Speaks raw
  /// OpenAI JSON over an <see cref="IModelApiConnection"/>; it does NOT extend the
  /// Docker API driver base (which is bound to the Docker socket + envelope).
  /// Streaming methods live in the <c>.Streaming.cs</c> partial.
  /// </summary>
  public partial class DockerApiModelInferenceDriver : IModelInferenceDriver
  {
    private readonly IModelApiConnection _connection;
    private readonly ModelRunnerEndpoint _endpoint;

    /// <summary>Initializes the driver.</summary>
    /// <param name="connection">The HTTP connection.</param>
    /// <param name="endpoint">The resolved endpoint (engine path source).</param>
    public DockerApiModelInferenceDriver(IModelApiConnection connection, ModelRunnerEndpoint endpoint)
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
      return await PostJsonAsync<EmbeddingsRequest, EmbeddingsResponse>(
          context, "/embeddings", request, "Embeddings", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<OpenAiModel>>> ListEngineModelsAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        using var response = await _connection.GetAsync(Path("/models"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
          return CommandResponse<IList<OpenAiModel>>.Fail(
              await SafeReadError(response, cancellationToken).ConfigureAwait(false),
              ErrorCodeFor(response.StatusCode),
              CreateApiErrorContext(context, "ListEngineModels", response),
              (int)response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync<OpenAiModelList>(stream, JsonHelper.CaseInsensitiveOptions, cancellationToken).ConfigureAwait(false);
        return CommandResponse<IList<OpenAiModel>>.Ok(list?.Data ?? []);
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
          return CommandResponse<TResponse>.Fail(
              await SafeReadError(response, cancellationToken).ConfigureAwait(false),
              ErrorCodeFor(response.StatusCode),
              CreateApiErrorContext(context, operation, response),
              (int)response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // A literal JSON `null` body (or empty stream) deserializes to null without throwing.
        // Treat it as a protocol/parse failure rather than a successful null payload — a null
        // response is never a valid OpenAI completion/embeddings result.
        var dto = await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonHelper.CaseInsensitiveOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new ModelRunnerException(
                $"{operation}: inference response body was null (expected JSON object)",
                ErrorCodes.ModelInference.StreamParseError,
                CreateApiErrorContext(context, operation, response));

        return CommandResponse<TResponse>.Ok(dto);
      }
      catch (ModelRunnerException)
      {
        // Already a typed model error (e.g. null/parse failure) — let it propagate unchanged
        // so callers see the precise inference error code, not a generic RequestFailed envelope.
        throw;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<TResponse>.Fail(ex.Message, ErrorCodes.ModelInference.RequestFailed);
      }
    }

    private string Path(string suffix) => _endpoint.EngineV1Path(suffix);

    // Accepts a nullable status so the streaming path can pass HttpRequestException.StatusCode
    // directly — a connect failure (no response, null status) maps to RequestFailed, matching
    // the non-streaming catch.
    private static string ErrorCodeFor(HttpStatusCode? code) => code switch
    {
      HttpStatusCode.Unauthorized => ErrorCodes.ModelInference.Unauthorized,
      HttpStatusCode.NotFound => ErrorCodes.ModelInference.ModelNotLoaded,
      _ => ErrorCodes.ModelInference.RequestFailed
    };

    private static async Task<string> SafeReadError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
      try
      {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
