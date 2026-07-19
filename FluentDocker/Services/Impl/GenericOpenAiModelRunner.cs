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
  public sealed class GenericOpenAiModelRunner : IModelRunner, IInferenceModelRunner
  {
    private static readonly DriverContext Ctx = new("openai");
    private const string Unsupported =
        "GenericOpenAiModelRunner supports inference only; model management/runtime control is not available for a generic endpoint.";

    private readonly IModelInferenceDriver _inference;
    private readonly IAsyncDisposable? _ownedResource;
    private readonly ModelRunnerEndpoint _endpoint;
    private readonly ModelReference? _defaultModel;
    private readonly InferenceModelId? _defaultInferenceId;
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
    /// <param name="defaultInferenceId">
    /// The verbatim inference model id used in the request body. When null it is derived
    /// from <paramref name="defaultModel"/> via <see cref="InferenceModelId.FromModelReference"/>
    /// (dropping the auto-injected <c>:latest</c>). Supply this directly to preserve a
    /// raw/remote id (e.g. <c>gpt-4o-mini</c>) verbatim.
    /// </param>
    public GenericOpenAiModelRunner(ModelRunnerEndpoint endpoint, ModelReference? defaultModel,
        IModelInferenceDriver inference, Func<CancellationToken, Task<bool>>? statusProbe = null, IAsyncDisposable? ownedResource = null,
        InferenceModelId? defaultInferenceId = null)
    {
      ArgumentNullException.ThrowIfNull(endpoint);
      ArgumentNullException.ThrowIfNull(inference);
      _endpoint = endpoint;
      _defaultModel = defaultModel;
      _defaultInferenceId = defaultInferenceId ?? InferenceModelId.FromModelReference(defaultModel);
      _inference = inference;
      _statusProbe = statusProbe;
      _ownedResource = ownedResource;
    }

    private readonly Func<CancellationToken, Task<bool>>? _statusProbe;

    /// <inheritdoc />
    public ModelReference? DefaultModel => _defaultModel;

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
      // A generic OpenAI-compatible endpoint can be backed by any engine (llama.cpp,
      // vLLM, a hosted service, …); the backend is genuinely unknown, so none is claimed.
      DefaultBackend = null,
      AvailableBackends = []
    };

    // ---- Inference (supported) ------------------------------------------------

    /// <inheritdoc />
    public async Task<ChatCompletionResponse> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      return Unwrap(await _inference.ChatCompletionAsync(Ctx, request, cancellationToken).ConfigureAwait(false), "Chat completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      return ModelRunnerInferenceHelpers.GuardDisposalAsync(
          _inference.ChatCompletionStreamAsync(Ctx, request, cancellationToken),
          () => Volatile.Read(ref _disposed) != 0, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompletionResponse> CompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      return Unwrap(await _inference.CompletionAsync(Ctx, request, cancellationToken).ConfigureAwait(false), "Completion");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
      return ModelRunnerInferenceHelpers.GuardDisposalAsync(
          _inference.CompletionStreamAsync(Ctx, request, cancellationToken),
          () => Volatile.Read(ref _disposed) != 0, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmbeddingsResponse> EmbeddingsAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(request);
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
      return await ModelRunnerInferenceHelpers.ChatAsync(
          this, _defaultInferenceId, prompt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      await foreach (var delta in ModelRunnerInferenceHelpers.ChatStreamAsync(
          this, _defaultInferenceId, prompt, cancellationToken).ConfigureAwait(false))
        yield return delta;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float>> EmbedAsync(string text, ModelReference? model = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return await ModelRunnerInferenceHelpers.EmbedAsync(
          this, _defaultInferenceId, text, model, cancellationToken).ConfigureAwait(false);
    }

    // ---- Management / runtime (not supported) ---------------------------------

    /// <inheritdoc />
    public Task<ModelInfo> PullAsync(ModelReference model, IProgress<ModelPullProgress>? progress = null, CancellationToken cancellationToken = default)
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
      var models = await _inference.ListEngineModelsAsync(Ctx, cancellationToken).ConfigureAwait(false);
      if (models.Success)
        return new ModelRunnerStatus { Running = true, Endpoint = _endpoint.BaseAddress };

      if (models.ExitCode is 404 or 405)
        return new ModelRunnerStatus
        {
          Running = false,
          Endpoint = _endpoint.BaseAddress,
          Error = $"Model-list probe returned HTTP {models.ExitCode} at '{_endpoint.EngineV1Path("/models")}'. Check the endpoint base path."
        };

      if (_statusProbe != null)
      {
        var reachable = await _statusProbe(cancellationToken).ConfigureAwait(false);
        return new ModelRunnerStatus
        {
          Running = reachable,
          Endpoint = _endpoint.BaseAddress,
          Error = reachable ? null : "Endpoint unreachable"
        };
      }

      return new ModelRunnerStatus { Running = false, Endpoint = _endpoint.BaseAddress, Error = models.Error };
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
    public Task LoadAsync(ModelReference model, ModelRunOptions? options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task UnloadAsync(ModelReference model, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task UnloadAllAsync(CancellationToken cancellationToken = default)
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
    public Task InstallRunnerAsync(ModelRunnerInstallOptions? options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw Fail();
    }

    /// <inheritdoc />
    public Task UninstallRunnerAsync(ModelRunnerUninstallOptions? options = null, CancellationToken cancellationToken = default)
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

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static FluentDockerNotSupportedException Fail() => new(Unsupported);

    private static T Unwrap<T>(CommandResponse<T> response, string operation)
    {
      if (response.Success)
        return response.Data!;

      throw new ModelRunnerException($"{operation} failed: {response.Error}", response.ErrorCode, response.ErrorContext);
    }
  }
}
