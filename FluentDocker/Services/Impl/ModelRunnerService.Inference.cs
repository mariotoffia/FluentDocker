using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Services.Impl
{
  public sealed partial class ModelRunnerService
  {
    /// <inheritdoc />
    public async Task<ChatCompletionResponse> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Inference().ChatCompletionAsync(Context(), request, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Chat completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return Inference().ChatCompletionStreamAsync(Context(), request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompletionResponse> CompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Inference().CompletionAsync(Context(), request, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return Inference().CompletionStreamAsync(Context(), request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmbeddingsResponse> EmbeddingsAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
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
}
