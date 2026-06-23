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
    private readonly IModelInferenceDriver _inferenceOverride;
    private readonly IAsyncDisposable _ownedResource;
    private ModelRunnerCapabilities _capabilities;
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
    public ModelRunnerService(FluentDockerKernel kernel, string driverId, ModelRunnerEndpoint endpoint,
        ModelReference defaultModel = null, IModelInferenceDriver inferenceOverride = null, IAsyncDisposable ownedResource = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(endpoint);
      _kernel = kernel;
      _driverId = driverId;
      _endpoint = endpoint;
      _defaultModel = defaultModel;
      _inferenceOverride = inferenceOverride;
      _ownedResource = ownedResource;
    }

    /// <inheritdoc />
    public ModelReference DefaultModel => _defaultModel;

    /// <inheritdoc />
    public Uri Endpoint => _endpoint.BaseAddress;

    /// <inheritdoc />
    public ModelRunnerCapabilities Capabilities => _capabilities ??= ComputeCapabilities();

    /// <inheritdoc />
    public async Task<string> ChatAsync(string prompt, CancellationToken cancellationToken = default)
    {
      var request = new ChatCompletionRequest
      {
        Model = _defaultModel?.ToString(),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      };

      var response = await ChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);
      return response.Choices is { Count: > 0 } ? response.Choices[0].Message?.Content : null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var request = new ChatCompletionRequest
      {
        Model = _defaultModel?.ToString(),
        Messages = new List<ChatMessage> { new() { Role = "user", Content = prompt } }
      };

      await foreach (var chunk in ChatCompletionStreamAsync(request, cancellationToken).ConfigureAwait(false))
      {
        var delta = chunk.Choices is { Count: > 0 } ? chunk.Choices[0].Delta?.Content : null;
        if (!string.IsNullOrEmpty(delta))
          yield return delta;
      }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float>> EmbedAsync(string text, ModelReference model = null, CancellationToken cancellationToken = default)
    {
      var request = new EmbeddingsRequest
      {
        Model = (model ?? _defaultModel)?.ToString(),
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
      var runtime = _kernel.TrySysCtl<IModelRuntimeDriver>(_driverId, out _);
      var inference = _inferenceOverride != null || _kernel.TrySysCtl<IModelInferenceDriver>(_driverId, out _);

      // The inference port speaks the OpenAI-compatible contract, which includes
      // streaming and embeddings — so a resolvable inference port advertises both.
      // Capabilities are declared from the contract, not probed per endpoint; an
      // OpenAI-compatible server missing an embeddings route would fail at call time.
      return new ModelRunnerCapabilities
      {
        SupportsManagement = management,
        SupportsRuntimeControl = runtime,
        SupportsInference = inference,
        SupportsStreaming = inference,
        SupportsEmbeddings = inference,
        SupportsPackaging = management,
        DefaultBackend = "llama.cpp",
        AvailableBackends = ["llama.cpp"]
      };
    }

    private IModelManagementDriver Management() => _kernel.SysCtl<IModelManagementDriver>(_driverId);

    private IModelRuntimeDriver Runtime() => _kernel.SysCtl<IModelRuntimeDriver>(_driverId);

    private IModelInferenceDriver Inference() => _inferenceOverride ?? _kernel.SysCtl<IModelInferenceDriver>(_driverId);

    private DriverContext Context() => new(_driverId);

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
