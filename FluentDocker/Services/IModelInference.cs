using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Services
{
  /// <summary>
  /// OpenAI-compatible inference: chat completions (uni + streaming), text
  /// completions and embeddings. Backed by the REST endpoint on :12434.
  /// The library does not perform built-in retry/backoff; callers own retry policy
  /// for retryable failures such as 429/503 or transient transport errors.
  /// </summary>
  public interface IModelInference
  {
    /// <summary>Performs a non-streaming chat completion.</summary>
    Task<ChatCompletionResponse> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Performs a streaming chat completion.</summary>
    IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Performs a non-streaming text completion.</summary>
    Task<CompletionResponse> CompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Performs a streaming text completion.</summary>
    IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Computes embeddings.</summary>
    Task<EmbeddingsResponse> EmbeddingsAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>The OpenAI <c>/models</c> listing as served by the engine.</summary>
    Task<IReadOnlyList<OpenAiModel>> ListEngineModelsAsync(CancellationToken cancellationToken = default);
  }
}
