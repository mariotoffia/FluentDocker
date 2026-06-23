using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Kernel;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Internal implementation of <see cref="IModelRunnerBuilder"/>. Resolves the
  /// model ports (management/runtime/inference) from the kernel-registered driver
  /// pack. When a custom endpoint is supplied via <see cref="WithEndpoint"/> it
  /// builds a dedicated inference connection bound to that endpoint; otherwise the
  /// pack's own (host) inference adapter is used.
  /// </summary>
  internal sealed class ModelRunnerBuilder(FluentDockerKernel kernel, string driverId) : IModelRunnerBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly string _driverId = driverId;
    private ModelReference _model;
    private int? _contextSize;
    private string _backend;
    private IReadOnlyList<string> _runtimeFlags;
    private ModelRunnerEndpoint _endpoint;
    private bool _pullIfMissing;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;

    /// <inheritdoc />
    public IModelRunnerBuilder ForModel(string reference) => ForModel(ModelReference.Parse(reference));

    /// <inheritdoc />
    public IModelRunnerBuilder ForModel(ModelReference reference)
    {
      _model = reference;
      return this;
    }

    /// <inheritdoc />
    public IModelRunnerBuilder WithContextSize(int tokens)
    {
      _contextSize = tokens;
      return this;
    }

    /// <inheritdoc />
    public IModelRunnerBuilder WithBackend(string backend)
    {
      _backend = backend;
      return this;
    }

    /// <inheritdoc />
    public IModelRunnerBuilder WithRuntimeFlags(params string[] flags)
    {
      _runtimeFlags = flags;
      return this;
    }

    /// <inheritdoc />
    public IModelRunnerBuilder WithEndpoint(ModelRunnerEndpoint endpoint)
    {
      _endpoint = endpoint;
      return this;
    }

    /// <inheritdoc />
    public IModelRunnerBuilder PullIfMissing(bool pull = true)
    {
      _pullIfMissing = pull;
      return this;
    }

    /// <inheritdoc />
    public IModelRunner Build() => Task.Run(() => BuildAsync()).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task<IModelRunner> BuildAsync(CancellationToken cancellationToken = default)
    {
      // A custom endpoint needs its own inference connection bound to that address;
      // for the default (host) endpoint the driver pack's inference adapter is used.
      IModelInferenceDriver inferenceOverride = null;
      IAsyncDisposable owned = null;
      if (_endpoint != null)
      {
        var connection = new ModelApiConnection(_endpoint, loggerFactory: _kernel.LoggerFactory);
        inferenceOverride = new DockerApiModelInferenceDriver(connection, _endpoint);
        owned = connection;
      }

      var endpoint = _endpoint ?? ModelRunnerEndpoint.Default();
      var runner = new ModelRunnerService(_kernel, _driverId, endpoint, _model, inferenceOverride, owned);

      if (_pullIfMissing && _model != null)
        await runner.PullAsync(_model, null, cancellationToken).ConfigureAwait(false);

      if (_model != null && NeedsConfigure())
        await runner.ConfigureAsync(_model, BuildConfigureOptions(), cancellationToken).ConfigureAwait(false);

      return runner;
    }

    private bool NeedsConfigure() => _contextSize.HasValue || !string.IsNullOrEmpty(_backend) || _runtimeFlags is { Count: > 0 };

    private ModelConfigureOptions BuildConfigureOptions() => new()
    {
      ContextSize = _contextSize,
      Backend = string.IsNullOrEmpty(_backend) ? default : ModelBackend.Custom(_backend),
      RuntimeFlags = _runtimeFlags
    };
  }
}
