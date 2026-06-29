using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// The kernel-backed <see cref="IModelRunner"/>. Resolves the model driver ports
  /// per <c>driverId</c> via <c>SysCtl</c>, translates <c>CommandResponse</c>
  /// failures into <see cref="ModelRunnerException"/>, computes
  /// <see cref="ModelRunnerCapabilities"/> from the resolvable ports, and adds
  /// ergonomic chat/embed helpers bound to a default model. Split into
  /// <c>.Store</c>/<c>.Engine</c>/<c>.Inference</c> partials.
  /// </summary>
  public sealed partial class ModelRunnerService : IModelRunner
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly ModelRunnerEndpoint _endpoint;
    private readonly ModelReference _defaultModel;
    private readonly InferenceModelId? _defaultInferenceId;
    private readonly IModelInferenceDriver _inferenceOverride;
    private readonly IAsyncDisposable _ownedResource;
    private ModelRunnerCapabilities _capabilities;
    private DriverContext _context;
    private int _disposed;

    /// <summary>Initializes the runner service.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="driverId">The driver id whose model ports to resolve.</param>
    /// <param name="endpoint">The resolved inference endpoint.</param>
    /// <param name="defaultModel">The default model bound at build time (nullable).</param>
    /// <param name="inferenceOverride">
    /// An inference driver bound to a custom endpoint, used instead of the port
    /// registered by the driver pack. When null, the pack's inference adapter
    /// (the OpenAI-compatible HTTP data plane for the host endpoint) is resolved
    /// from the kernel.
    /// </param>
    /// <param name="ownedResource">
    /// A resource owned by this service (e.g. the inference connection), disposed
    /// when the service is disposed.
    /// </param>
    /// <param name="defaultInferenceId">
    /// The verbatim inference model id used in the OpenAI-compatible request body.
    /// When null it is derived from <paramref name="defaultModel"/> via
    /// <see cref="InferenceModelId.FromModelReference"/> (dropping the auto-injected
    /// <c>:latest</c>). Supply this directly to preserve a raw/remote id verbatim.
    /// </param>
    public ModelRunnerService(FluentDockerKernel kernel, string driverId, ModelRunnerEndpoint endpoint,
        ModelReference defaultModel = null, IModelInferenceDriver inferenceOverride = null, IAsyncDisposable ownedResource = null,
        InferenceModelId? defaultInferenceId = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(endpoint);
      _kernel = kernel;
      _driverId = driverId;
      _endpoint = endpoint;
      _defaultModel = defaultModel;
      _defaultInferenceId = defaultInferenceId ?? InferenceModelId.FromModelReference(defaultModel);
      _inferenceOverride = inferenceOverride;
      _ownedResource = ownedResource;
    }

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
    public ModelRunnerCapabilities Capabilities => _capabilities ??= ComputeCapabilities();

    /// <inheritdoc />
    public async Task<string> ChatAsync(string prompt, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(prompt);
      var request = new ChatCompletionRequest
      {
        Model = RequireModelId(),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      };

      var response = await ChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);
      return (response.Choices is { Count: > 0 } ? response.Choices[0].Message?.Content : null)
          ?? throw new ModelRunnerException("Chat completion returned no content (model produced no choices).",
              ErrorCodes.ModelInference.RequestFailed);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(prompt);
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
      ArgumentNullException.ThrowIfNull(text);
      var request = new EmbeddingsRequest
      {
        Model = RequireModelId(model),
        Input = new List<string> { text }
      };

      var response = await EmbeddingsAsync(request, cancellationToken).ConfigureAwait(false);
      return response.Data is { Count: > 0 } ? ToReadOnly(response.Data[0].Embedding) : [];
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

    private ModelRunnerCapabilities ComputeCapabilities()
    {
      var management = _kernel.TrySysCtl<IModelManagementDriver>(_driverId, out _);
      var runtime = _kernel.TrySysCtl<IModelRuntimeDriver>(_driverId, out var rt);
      var hasInferencePort = _kernel.TrySysCtl<IModelInferenceDriver>(_driverId, out var inf);
      var inference = _inferenceOverride != null || hasInferencePort;

      // The inference port speaks the OpenAI-compatible contract, which includes
      // streaming and embeddings — so a resolvable inference port advertises both.
      // Capabilities are declared from the contract, not probed per endpoint; an
      // OpenAI-compatible server missing an embeddings route would fail at call time.
      //
      // The backend engine is NOT assumed here: custom inference overrides are isolated
      // from the scoped runtime; otherwise the resolved inference/runtime ports advertise
      // it when they implement IModelBackendInfo.
      var backend = _inferenceOverride != null
          ? _inferenceOverride as IModelBackendInfo
          : (inf as IModelBackendInfo) ?? (rt as IModelBackendInfo);

      return new ModelRunnerCapabilities
      {
        SupportsManagement = management,
        SupportsRuntimeControl = runtime,
        SupportsInference = inference,
        SupportsStreaming = inference,
        SupportsEmbeddings = inference,
        SupportsPackaging = management,
        DefaultBackend = backend?.DefaultBackend,
        AvailableBackends = backend?.AvailableBackends ?? []
      };
    }

    private string RequireModelId(ModelReference model = null)
    {
      // The inference body carries the VERBATIM id (no auto ":latest"). A per-call
      // Docker reference is reduced to its inference form; otherwise the bound default
      // inference id is used (itself derived verbatim from the default model or a raw id).
      var id = model != null
          ? InferenceModelId.FromModelReference(model)?.Value
          : _defaultInferenceId?.Value;

      if (string.IsNullOrEmpty(id))
        throw new ArgumentException(
          "No model specified and no default model was configured. Pass a model or configure one via ForModel/WithModel.", nameof(model));
      return id;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static string Unsupported(string capability) =>
        $"This runner's driver does not support {capability}; check Capabilities before calling.";

    private IModelManagementDriver Management() =>
        _kernel.TrySysCtl<IModelManagementDriver>(_driverId, out var d)
            ? d
            : throw new NotSupportedException(Unsupported("model management"));

    private IModelRuntimeDriver Runtime() =>
        _kernel.TrySysCtl<IModelRuntimeDriver>(_driverId, out var d)
            ? d
            : throw new NotSupportedException(Unsupported("runtime control"));

    private IModelInferenceDriver Inference() =>
        _inferenceOverride ?? (_kernel.TrySysCtl<IModelInferenceDriver>(_driverId, out var d)
            ? d
            : throw new NotSupportedException(Unsupported("inference")));

    // Returns the registered DriverContext (carrying host/TLS/logger), falling back to a
    // bare context when the scoped driver isn't registered so a directly-constructed or
    // unregistered-mid-life runner degrades instead of throwing DriverNotFoundException.
    // The context is cached for the (build-scoped) runner lifetime; a driver re-registered
    // with a new context under a live runner is not tracked, which is acceptable here.
    private DriverContext Context() => _context ??=
        _kernel.Registry.IsRegistered(_driverId)
            ? _kernel.Registry.GetContext(_driverId)
            : new DriverContext(_driverId);

    private static T Unwrap<T>(CommandResponse<T> response, string operation)
    {
      if (response.Success)
        return response.Data;

      throw new ModelRunnerException($"{operation} failed: {response.Error}", response.ErrorCode, response.ErrorContext);
    }

    private static void UnwrapUnit(CommandResponse<Unit> response, string operation)
    {
      if (!response.Success)
        throw new ModelRunnerException($"{operation} failed: {response.Error}", response.ErrorCode, response.ErrorContext);
    }

    private static IReadOnlyList<T> ToReadOnly<T>(IList<T> list) => list as IReadOnlyList<T> ?? [.. list ?? []];
  }
}
