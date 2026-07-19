#nullable disable warnings
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
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
      var generation = ProvisionGeneration;
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseModel(_model);
      _configure?.Invoke(builder);

      var service = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        await service.StartAsync(cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        await service.DisposeAsync().ConfigureAwait(false);
        throw;
      }

      if (TryCommitProvision(generation, () =>
      {
        _service = service;
        ResourceName = service.Name;
      }))
      {
        return;
      }

      await RemoveStaleModelAsync(service, generation).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      var service = _service;
      if (service == null)
        return;

      // DisposeAsync() honors KeepRunning(), but its unload failures are logged/swallowed —
      // cleanup could then report success while the model is still resident. So unless the model
      // is kept, run the STRICT StopAsync first (it surfaces an unload failure as an exception).
      // DisposeAsync() afterwards only releases the runner (state is now Stopped, so it does not
      // unload again). Both are raced against the teardown timeout so a hung unload cannot block
      // CI. On a StopAsync failure we leave _service set so the next DisposeAsync RETRIES the
      // strict stop: a failed stop moves state to Unknown (still "maybe resident"), so we strict-
      // stop for ANY state that is not a confirmed Stopped/Removed — not only Running. Keying off
      // Running alone would make the retry silently fall through to the swallowing DisposeAsync.
      if (!service.KeepRunning &&
          service.State != ServiceRunningState.Stopped &&
          service.State != ServiceRunningState.Removed)
        await service.StopAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);

      await service.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
      _service = null;
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var service = _service;
      if (service == null)
        return;

      // Cannot reuse service.DisposeAsync() here. It is idempotent
      // (Interlocked.CompareExchange on _disposed) and the graceful TeardownAsync
      // already set _disposed=1, so a second DisposeAsync() short-circuits to a
      // no-op and returns INSTANT success even while the original unload is still
      // hung — faking recovery. Force-remove must do real work, so go straight to
      // the runtime driver's UnloadAsync, bounded by the token: a genuinely hung
      // unload then fails the force (surfaced by ResourceBase as ForceRemoveException)
      // instead of being silently swallowed.
      var driver = Kernel.SysCtl<IModelRuntimeDriver>(DriverId);
      var response = await driver
          .UnloadAsync(new DriverContext(DriverId), _model, cancellationToken)
          .WaitAsync(cancellationToken)
          .ConfigureAwait(false);

      if (!response.Success)
        throw new ModelRunnerException(
            $"Force unload failed: {response.Error}", response.ErrorCode, response.ErrorContext);

      // The model is now confirmed unloaded, but the service still owns a runner (inference
      // connection / HttpClient / X509 cert). Dispose the runner DIRECTLY: IModelRunner.DisposeAsync
      // only releases those owned resources — it does NOT re-unload — so there is no risk of
      // re-entering the hung-unload path we deliberately bypassed above. Without this, clearing
      // _service alone would leak the runner's owned resources. Bounded by the token like the unload.
      await service.Runner.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

      // Only clear after the CONFIRMED unload + runner disposal, so a failure keeps the
      // resource provisioned for retry.
      _service = null;
    }

    private void EnsureInitialized()
    {
      if (!IsInitialized || _service == null)
        throw new InvalidOperationException(
            "Model resource is not initialized. Call InitializeAsync first.");
    }

    private async Task RemoveStaleModelAsync(IModelService service, int generation)
    {
      if (!ShouldCleanupRejectedProvision(generation))
        return;

      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        if (!service.KeepRunning &&
            service.State != ServiceRunningState.Stopped &&
            service.State != ServiceRunningState.Removed)
        {
          var stopTask = service.StopAsync(cts.Token);
          await stopTask.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        await service.DisposeAsync().AsTask().WaitAsync(cts.Token).ConfigureAwait(false);
      }
      catch
      {
        OrphanCleanup.MarkAbandonedLateProvision(_model.ToString(), Options.SessionId);
      }
    }
  }
}
