using System;
using System.Threading;
using System.Threading.Tasks;
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
    public IModelService Build() => Task.Run(BuildAsync).GetAwaiter().GetResult();

    private async Task<IModelService> BuildAsync()
    {
      if (_model == null)
        throw new InvalidOperationException("A model reference is required (use UseModel(reference)).");

      var runnerBuilder = new ModelRunnerBuilder(_kernel, _driverId).ForModel(_model);
      if (_contextSize.HasValue)
        runnerBuilder.WithContextSize(_contextSize.Value);
      if (!string.IsNullOrEmpty(_backend))
        runnerBuilder.WithBackend(_backend);
      if (_pullIfMissing)
        runnerBuilder.PullIfMissing();

      var runner = await runnerBuilder.BuildAsync(CancellationToken.None).ConfigureAwait(false);
      return new ModelService(_kernel, _driverId, _model, runner, _runOptions, _keepRunning);
    }
  }
}
