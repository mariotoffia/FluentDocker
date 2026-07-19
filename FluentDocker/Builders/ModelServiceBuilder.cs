#nullable disable warnings
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Kernel;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Internal implementation of <see cref="IModelServiceBuilder"/>.
  /// </summary>
  internal sealed class ModelServiceBuilder(FluentDockerKernel kernel, string driverId) : IModelServiceBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly string _driverId = driverId;
    private ModelReference _model;
    private int? _contextSize;
    private string _backend;
    private string[] _runtimeFlags;
    private ModelRunnerEndpoint _endpoint;
    private ModelApiConnectionConfig _config;
    private string _apiKey;
    private IModelInferenceDriver _inferenceDriver;
    private string _inferenceDriverId;
    private ModelRunOptions _runOptions;
    private bool _keepRunning;
    private bool _pullIfMissing;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;

    internal ModelServiceBuilder ForModel(ModelReference model)
    {
      _model = model;
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithContextSize(int tokens)
    {
      if (tokens <= 0)
        throw new ArgumentOutOfRangeException(nameof(tokens), tokens, "Context size must be greater than zero.");

      _contextSize = tokens;
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithBackend(string backend)
    {
      _backend = backend;
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithRuntimeFlags(params string[] flags)
    {
      _runtimeFlags = flags;
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithEndpoint(ModelRunnerEndpoint endpoint,
        ModelApiConnectionConfig? config = null, string? apiKey = null)
    {
      ThrowIfInferenceRouteConflict(_inferenceDriver != null || _inferenceDriverId != null);
      _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
      _config = config;
      _apiKey = apiKey;
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithInferenceDriver(IModelInferenceDriver inference)
    {
      ThrowIfInferenceRouteConflict(_endpoint != null);
      _inferenceDriver = inference ?? throw new ArgumentNullException(nameof(inference));
      _inferenceDriverId = null;           // last call wins
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithInferenceDriver(string driverId)
    {
      ThrowIfInferenceRouteConflict(_endpoint != null);
      _inferenceDriverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
      _inferenceDriver = null;             // last call wins
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder WithRunOptions(Action<ModelRunOptionsBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      var builder = new ModelRunOptionsBuilder();
      configure(builder);
      _runOptions = builder.Build();
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder KeepRunning(bool keep = true)
    {
      _keepRunning = keep;
      return this;
    }

    /// <inheritdoc />
    public IModelServiceBuilder PullIfMissing(bool pull = true)
    {
      _pullIfMissing = pull;
      return this;
    }

    /// <inheritdoc />
    public IModelService Build() => Task.Run(() => BuildAsync(CancellationToken.None)).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task<IModelService> BuildAsync(CancellationToken cancellationToken = default)
    {
      if (_model == null)
        throw new InvalidOperationException("A model reference is required (use UseModel(reference)).");

      var runnerBuilder = new ModelRunnerBuilder(_kernel, _driverId).ForModel(_model);
      if (_contextSize.HasValue)
        runnerBuilder.WithContextSize(_contextSize.Value);
      if (!string.IsNullOrEmpty(_backend))
        runnerBuilder.WithBackend(_backend);
      if (_runtimeFlags is { Length: > 0 })
        runnerBuilder.WithRuntimeFlags(_runtimeFlags);
      // Inference override precedence mirrors IModelRunnerBuilder: explicit driver, then
      // a registered driver id, then a custom endpoint (+config/apiKey).
      if (_inferenceDriver != null)
        runnerBuilder.WithInferenceDriver(_inferenceDriver);
      else if (_inferenceDriverId != null)
        runnerBuilder.WithInferenceDriver(_inferenceDriverId);
      else if (_endpoint != null)
        runnerBuilder.WithEndpoint(_endpoint, _config, _apiKey);
      if (_pullIfMissing)
        runnerBuilder.PullIfMissing();

      // Pass the caller's token through to the build-time pull/configure work.
      var runner = await runnerBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);
      return new ModelService(_kernel, _driverId, _model, runner, _runOptions, _keepRunning);
    }

    private static void ThrowIfInferenceRouteConflict(bool hasConflict)
    {
      if (hasConflict)
        throw new InvalidOperationException(
            "WithEndpoint cannot be combined with WithInferenceDriver; configure exactly one inference route.");
    }
  }
}
