using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Models;
using FluentDocker.Services;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A single Docker Model Runner model with async test-resource lifecycle.
  /// </summary>
  public sealed class ModelResource : ResourceBase
  {
    private readonly ModelReference _model;
    private readonly Action<IModelServiceBuilder> _configure;
    private IModelService _service;

    /// <summary>
    /// Creates a model resource from a model reference string.
    /// </summary>
    public ModelResource(
        FluentDockerKernel kernel,
        string model,
        Action<IModelServiceBuilder> configure = null,
        DockerResourceOptions options = null)
        : this(kernel, ModelReference.Parse(model), configure, options)
    {
    }

    /// <summary>
    /// Creates a model resource from a parsed model reference.
    /// </summary>
    public ModelResource(
        FluentDockerKernel kernel,
        ModelReference model,
        Action<IModelServiceBuilder> configure = null,
        DockerResourceOptions options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(model);
      _model = model;
      _configure = configure;
    }

    /// <summary>
    /// The started model service, available after initialization.
    /// </summary>
    public IModelService Service
    {
      get
      {
        EnsureInitialized();
        return _service;
      }
    }

    /// <summary>
    /// The model runner bound to <see cref="Model"/>, available after initialization.
    /// </summary>
    public IModelRunner Runner => Service.Runner;

    /// <summary>
    /// The model reference this resource manages. Known from construction, so it is
    /// readable before initialization (e.g. for logging the target model).
    /// </summary>
    public ModelReference Model => _model;

    /// <inheritdoc />
    protected override Task PreflightAsync(CancellationToken cancellationToken)
    {
      Kernel.SysCtl<IModelRuntimeDriver>(DriverId);
      return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseModel(_model);
      _configure?.Invoke(builder);

      _service = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
      await _service.StartAsync(cancellationToken).ConfigureAwait(false);
      ResourceName = _service.Name;
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      var service = _service;
      if (service == null)
        return;

      await service.DisposeAsync().ConfigureAwait(false);
      _service = null;
    }

    /// <inheritdoc />
    protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      return TeardownAsync(cancellationToken);
    }

    private void EnsureInitialized()
    {
      if (!IsInitialized || _service == null)
        throw new InvalidOperationException(
            "Model resource is not initialized. Call InitializeAsync first.");
    }
  }
}
