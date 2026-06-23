using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Hexagonal port for the OpenAI-compatible inference data plane (chat /
  /// completion / embeddings / engine-model list). Backed by the HTTP adapter
  /// (<c>:12434/engines/…/v1</c>).
  /// </summary>
  /// <remarks>
  /// Streaming methods return <see cref="IAsyncEnumerable{T}"/> directly (not a
  /// <see cref="CommandResponse{T}"/>); per-call failures surface as
  /// <c>ModelRunnerException</c> thrown during enumeration, because a
  /// <see cref="CommandResponse{T}"/> envelope cannot represent a mid-stream fault.
  /// </remarks>
  public interface IModelInferenceDriver
  {
    /// <summary>Performs a non-streaming chat completion.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The chat request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The chat completion response.</returns>
    Task<CommandResponse<ChatCompletionResponse>> ChatCompletionAsync(
        DriverContext context, ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Performs a streaming chat completion.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The chat request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async stream of chat completion chunks.</returns>
    IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
        DriverContext context, ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Performs a non-streaming text completion.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The completion request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The completion response.</returns>
    Task<CommandResponse<CompletionResponse>> CompletionAsync(
        DriverContext context, CompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Performs a streaming text completion.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The completion request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async stream of completion chunks.</returns>
    IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(
        DriverContext context, CompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Computes embeddings.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The embeddings request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The embeddings response.</returns>
    Task<CommandResponse<EmbeddingsResponse>> EmbeddingsAsync(
        DriverContext context, EmbeddingsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the models served by the engine (the OpenAI <c>/models</c> listing).</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The engine models.</returns>
    Task<CommandResponse<IList<OpenAiModel>>> ListEngineModelsAsync(
        DriverContext context, CancellationToken cancellationToken = default);
  }
}
