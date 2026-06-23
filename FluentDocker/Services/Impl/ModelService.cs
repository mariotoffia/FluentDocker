using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// A single-model lifecycle handle (<see cref="IModelService"/>): <c>StartAsync</c>
  /// loads the model, <c>StopAsync</c> unloads it, <c>RemoveAsync</c> removes it,
  /// participating in the same <see cref="ServiceRunningState"/> machine and hook
  /// pipeline as containers/volumes. Optionally unloads the model on dispose.
  /// </summary>
  public sealed class ModelService : IModelService, IServiceCapabilities
  {
    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<ModelService> _logger;
    private readonly string _driverId;
    private readonly ModelReference _model;
    private readonly IModelRunner _runner;
    private readonly ModelRunOptions _runOptions;
    private readonly bool _keepRunning;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = [];
    private readonly Dictionary<ServiceRunningState, List<Func<IServiceAsync, Task>>> _stateHooks = [];
    private ServiceRunningState _state = ServiceRunningState.Unknown;
    private int _disposed;

    /// <summary>Initializes the model service.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="driverId">The driver id.</param>
    /// <param name="model">The model this service manages.</param>
    /// <param name="runner">The runner bound to this model.</param>
    /// <param name="runOptions">Options for loading (StartAsync).</param>
    /// <param name="keepRunning">When true, the model is NOT unloaded on dispose.</param>
    public ModelService(FluentDockerKernel kernel, string driverId, ModelReference model,
        IModelRunner runner, ModelRunOptions runOptions = null, bool keepRunning = false)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(model);
      ArgumentNullException.ThrowIfNull(runner);

      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<ModelService>();
      _driverId = driverId;
      _model = model;
      _runner = runner;
      _runOptions = runOptions;
      _keepRunning = keepRunning;

      foreach (var state in Enum.GetValues<ServiceRunningState>())
        _stateHooks[state] = [];
    }

    /// <inheritdoc />
    public string Name => _model.ToString();

    /// <inheritdoc />
    public ServiceRunningState State => _state;

    /// <inheritdoc />
    public FluentDockerKernel Kernel => _kernel;

    /// <inheritdoc />
    public string DriverId => _driverId;

    /// <inheritdoc />
    public ModelReference Model => _model;

    /// <inheritdoc />
    public IModelRunner Runner => _runner;

    /// <inheritdoc />
    public bool CanStart => true;

    /// <inheritdoc />
    public bool CanStop => true;

    /// <inheritdoc />
    public bool CanPause => false;

    /// <inheritdoc />
    public bool CanRemove => true;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design (mirrors IServiceAsync)
    /// <inheritdoc />
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      UpdateState(ServiceRunningState.Starting);
      await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

      await _runner.LoadAsync(_model, _runOptions, cancellationToken).ConfigureAwait(false);

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Models cannot be paused; use Stop (unload) instead.");

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      UpdateState(ServiceRunningState.Stopping);
      await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

      await _runner.UnloadAsync(_model, false, cancellationToken).ConfigureAwait(false);

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

      await _runner.RemoveAsync(_model, force, cancellationToken).ConfigureAwait(false);

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ModelInfo> InspectAsync(CancellationToken cancellationToken = default) =>
        _runner.InspectAsync(_model, cancellationToken);

    /// <inheritdoc />
    public Task ConfigureAsync(ModelConfigureOptions options, CancellationToken cancellationToken = default) =>
        _runner.ConfigureAsync(_model, options, cancellationToken);

    /// <inheritdoc />
    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ArgumentNullException.ThrowIfNull(hook);
      _stateHooks[state].Add(hook);
      _hooks[uniqueName ?? Guid.NewGuid().ToString()] = hook;
      return this;
    }

    /// <inheritdoc />
    public IServiceAsync RemoveHook(string uniqueName)
    {
      if (uniqueName != null && _hooks.Remove(uniqueName, out var hook))
      {
        foreach (var list in _stateHooks.Values)
          list.Remove(hook);
      }

      return this;
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      DisposeCoreAsync().AsTask().GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      await DisposeCoreAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeCoreAsync()
    {
      if (_keepRunning || _state != ServiceRunningState.Running)
        return;

      try
      {
        await StopAsync().ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "ModelService dispose unload failed for '{Model}'", _model);
      }
    }

    private void UpdateState(ServiceRunningState newState)
    {
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      if (!_stateHooks.TryGetValue(state, out var hooks))
        return;

      foreach (var hook in hooks)
      {
        try
        {
          await hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ModelService hook execution failed");
        }
      }
    }
  }
}
