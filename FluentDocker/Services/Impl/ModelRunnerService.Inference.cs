using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services;

namespace FluentDocker.Services.Impl
{
  public sealed partial class ModelRunnerService
  {
    /// <inheritdoc />
    public async Task<ChatCompletionResponse> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      var response = await Inference().ChatCompletionAsync(Context(), request, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Chat completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      return Inference().ChatCompletionStreamAsync(Context(), request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompletionResponse> CompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      var response = await Inference().CompletionAsync(Context(), request, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      return Inference().CompletionStreamAsync(Context(), request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmbeddingsResponse> EmbeddingsAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      var response = await Inference().EmbeddingsAsync(Context(), request, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Embeddings");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenAiModel>> ListEngineModelsAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Inference().ListEngineModelsAsync(Context(), cancellationToken).ConfigureAwait(false);
      return ToReadOnly(Unwrap(response, "List engine models"));
    }
  }

  internal static class ModelRunnerInferenceHelpers
  {
    public static async Task<string> ChatAsync(
        IModelInference inference, InferenceModelId? defaultInferenceId, string prompt,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(inference);
      ArgumentNullException.ThrowIfNull(prompt);
      var response = await inference.ChatCompletionAsync(new ChatCompletionRequest
      {
        Model = RequireModelId(defaultInferenceId),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      }, cancellationToken).ConfigureAwait(false);

      if (response.Choices is not { Count: > 0 })
        throw new ModelRunnerException(
            "Chat completion returned no choices.", ErrorCodes.ModelInference.RequestFailed);
      return response.Choices[0].Message?.Content
          ?? throw new ModelRunnerException(
              "Chat completion choice carried no text content (e.g. tool_calls or a refusal); use ChatCompletionAsync to read the full message.",
              ErrorCodes.ModelInference.RequestFailed);
    }

    public static async IAsyncEnumerable<string> ChatStreamAsync(
        IModelInference inference, InferenceModelId? defaultInferenceId, string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(inference);
      ArgumentNullException.ThrowIfNull(prompt);
      var request = new ChatCompletionRequest
      {
        Model = RequireModelId(defaultInferenceId),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      };

      await foreach (var chunk in inference.ChatCompletionStreamAsync(request, cancellationToken).ConfigureAwait(false))
      {
        var delta = chunk?.Choices is { Count: > 0 } ? chunk.Choices[0]?.Delta?.Content : null;
        if (!string.IsNullOrEmpty(delta))
          yield return delta;
      }
    }

    public static async Task<IReadOnlyList<float>> EmbedAsync(
        IModelInference inference, InferenceModelId? defaultInferenceId, string text,
        ModelReference model = null, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(inference);
      ArgumentNullException.ThrowIfNull(text);
      var response = await inference.EmbeddingsAsync(new EmbeddingsRequest
      {
        Model = RequireModelId(defaultInferenceId, model),
        Input = new List<string> { text }
      }, cancellationToken).ConfigureAwait(false);

      return response.Data is { Count: > 0 } ? ToReadOnly(response.Data[0].Embedding) : [];
    }

    private static string RequireModelId(InferenceModelId? defaultInferenceId, ModelReference model = null)
    {
      var id = model != null
          ? InferenceModelId.FromModelReference(model)?.Value
          : defaultInferenceId?.Value;

      if (string.IsNullOrEmpty(id))
        throw new ArgumentException(
          "No model specified and no default model was configured. Pass a model or configure one via ForModel/WithModel.", nameof(model));
      return id;
    }

    private static IReadOnlyList<T> ToReadOnly<T>(IList<T> list) => list as IReadOnlyList<T> ?? [.. list ?? []];
  }
}
