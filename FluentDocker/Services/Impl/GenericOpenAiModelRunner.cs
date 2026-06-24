using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// An inference-only <see cref="IModelRunner"/> targeting any OpenAI-compatible
  /// endpoint (DMR, a bare llama-server, vLLM, LM Studio, or a hosted endpoint).
  /// It owns its <see cref="FluentDocker.Drivers.Models.Connection.IModelApiConnection"/>; management / runtime-control
  /// operations are not supported (<see cref="ModelRunnerCapabilities.SupportsManagement"/>
  /// is <c>false</c>).
  /// </summary>
  public sealed class GenericOpenAiModelRunner : IModelRunner
  {
    private static readonly DriverContext Ctx = new("openai");
    private const string Unsupported =
        "GenericOpenAiModelRunner supports inference only; model management/runtime control is not available for a generic endpoint.";

    private readonly IModelInferenceDriver _inference;
    private readonly IAsyncDisposable _ownedResource;
    private readonly ModelRunnerEndpoint _endpoint;
    private readonly ModelReference _defaultModel;
    private int _disposed;

    /// <summary>
    /// Creates an inference-only runner over an inference port (composition is done
    /// by the caller — e.g. <see cref="ModelRunnerEnvironment"/> — so this service
    /// type stays free of adapter dependencies).
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="defaultModel">The default model (nullable).</param>
    /// <param name="inference">The inference driver port.</param>
    /// <param name="statusProbe">Optional reachability probe for <c>StatusAsync</c> (e.g. the connection's ping).</param>
    /// <param name="ownedResource">A resource (e.g. the connection) owned and disposed by this runner.</param>
    public GenericOpenAiModelRunner(ModelRunnerEndpoint endpoint, ModelReference defaultModel,
        IModelInferenceDriver inference, Func<CancellationToken, Task<bool>> statusProbe = null, IAsyncDisposable ownedResource = null)
    {
      ArgumentNullException.ThrowIfNull(endpoint);
      ArgumentNullException.ThrowIfNull(inference);
      _endpoint = endpoint;
      _defaultModel = defaultModel;
      _inference = inference;
      _statusProbe = statusProbe;
      _ownedResource = ownedResource;
    }

    private readonly Func<CancellationToken, Task<bool>> _statusProbe;

    /// <inheritdoc />
    public ModelReference DefaultModel => _defaultModel;

    /// <inheritdoc />
    /// <remarks>
    /// Returns the base authority only (scheme://host:port). Engine path, env-injected
    /// raw path, and unix-socket detail held by the underlying ModelRunnerEndpoint are
    /// not exposed through this Uri.
    /// </remarks>
    public Uri Endpoint => _endpoint.BaseAddress;

    /// <inheritdoc />
    public ModelRunnerCapabilities Capabilities => new()
    {
      SupportsManagement = false,
      SupportsRuntimeControl = false,
      SupportsInference = true,
      SupportsStreaming = true,
      SupportsEmbeddings = true,
      SupportsPackaging = false,
      DefaultBackend = "llama.cpp",
      AvailableBackends = ["llama.cpp"]
    };

    // ---- Inference (supported) ------------------------------------------------

    /// <inheritdoc />
    public async Task<ChatCompletionResponse> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return Unwrap(await _inference.ChatCompletionAsync(Ctx, request, cancellationToken).ConfigureAwait(false), "Chat completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return _inference.ChatCompletionStreamAsync(Ctx, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompletionResponse> CompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return Unwrap(await _inference.CompletionAsync(Ctx, request, cancellationToken).ConfigureAwait(false), "Completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return _inference.CompletionStreamAsync(Ctx, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmbeddingsResponse> EmbeddingsAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return Unwrap(await _inference.EmbeddingsAsync(Ctx, request, cancellationToken).ConfigureAwait(false), "Embeddings");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenAiModel>> ListEngineModelsAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var data = Unwrap(await _inference.ListEngineModelsAsync(Ctx, cancellationToken).ConfigureAwait(false), "List engine models");
      return data as IReadOnlyList<OpenAiModel> ?? [.. data ?? []];
    }

    // ---- Ergonomics -----------------------------------------------------------

    /// <inheritdoc />
    public async Task<string> ChatAsync(string prompt, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await ChatCompletionAsync(new ChatCompletionRequest
      {
        Model = RequireModelId(),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      }, cancellationToken).ConfigureAwait(false);

      return response.Choices is { Count: > 0 } ? response.Choices[0].Message?.Content : null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var request = new ChatCompletionRequest
      {
        Model = RequireModelId(),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      };

      await foreach (var chunk in ChatCompletionStreamAsync(request, cancellationToken).ConfigureAwait(false))
      {
        var delta = chunk?.Choices is { Count: > 0 } ? chunk.Choices[0]?.Delta?.Content : null;
        if (!string.IsNullOrEmpty(delta))
          yield return delta;
      }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float>> EmbedAsync(string text, ModelReference model = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await EmbeddingsAsync(new EmbeddingsRequest
      {
        Model = RequireModelId(model),
        Input = new List<string> { text }
      }, cancellationToken).ConfigureAwait(false);

      return response.Data is { Count: > 0 }
          ? response.Data[0].Embedding as IReadOnlyList<float> ?? [.. response.Data[0].Embedding ?? []]
          : [];
    }

    // ---- Management / runtime (not supported) ---------------------------------

    /// <inheritdoc />
    public Task<ModelInfo> PullAsync(ModelReference model, IProgress<ModelPullProgress> progress = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task<ModelInfo> InspectAsync(ModelReference model, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task RemoveAsync(ModelReference model, bool force = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task TagAsync(ModelReference source, ModelReference target, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task PushAsync(ModelReference model, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task<ModelInfo> PackageAsync(ModelPackageRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task<ModelPruneResult> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task<ModelDiskUsage> DiskUsageAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public async Task<ModelRunnerStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var running = _statusProbe == null || await _statusProbe(cancellationToken).ConfigureAwait(false);
      return new ModelRunnerStatus { Running = running, Endpoint = _endpoint.BaseAddress, Error = running ? null : "Endpoint unreachable" };
    }

    /// <inheritdoc />
    public Task<ModelRunnerVersion> VersionAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunningModel>> ListRunningAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task LoadAsync(ModelReference model, ModelRunOptions options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task UnloadAsync(ModelReference model, bool all = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task ConfigureAsync(ModelReference model, ModelConfigureOptions options, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> LogsAsync(bool follow = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task InstallRunnerAsync(ModelRunnerInstallOptions options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      if (_ownedResource != null)
        await _ownedResource.DisposeAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private string RequireModelId(ModelReference model = null)
    {
      var id = (model ?? _defaultModel)?.ToString();
      if (string.IsNullOrEmpty(id))
        throw new ArgumentException(
          "No model specified and no default model was configured. Pass a model or configure one via ForModel/WithModel.", nameof(model));
      return id;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static NotSupportedException Fail() => new(Unsupported);

    private static T Unwrap<T>(CommandResponse<T> response, string operation)
    {
      if (response.Success)
        return response.Data;

      throw new ModelRunnerException($"{operation} failed: {response.Error}", response.ErrorCode, response.ErrorContext);
    }
  }
}
