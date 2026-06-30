using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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
    // Set just before the load attempt. A load that partially loads the model and then
    // faults/cancels leaves _state == Unknown (not Running), so dispose must key off this
    // flag — not only _state == Running — to avoid leaking a resident model.
    private bool _loadInitiated;
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
    public bool KeepRunning => _keepRunning;

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
      ThrowIfDisposed();
      UpdateState(ServiceRunningState.Starting);
      await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

      try
      {
        // Serialize load on the per-model gate so a concurrent load/unload/pull of the SAME
        // model cannot race; different models proceed in parallel.
        await using var gate = await ModelOperationGate.AcquireAsync(_model, cancellationToken).ConfigureAwait(false);
        _loadInitiated = true;
        // Hold the gate for the FULL load — no .WaitAsync escape hatch. The driver honors the
        // token, so a cancel ends the load and releases the gate together. Releasing the gate
        // while a load was still in flight (the old .WaitAsync did exactly that on cancel) would
        // let a concurrent load/unload/pull of the same model race it — defeating the gate.
        await _runner.LoadAsync(_model, _runOptions, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      throw new NotSupportedException("Models cannot be paused; use Stop (unload) instead.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return StopCoreAsync(cancellationToken);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken = default)
    {
      UpdateState(ServiceRunningState.Stopping);
      await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

      try
      {
        // Serialize unload on the same per-model gate the load path uses, holding it for the
        // FULL unload (the driver honors the token). The old .WaitAsync released the gate on
        // cancel while the unload was still running, which let a concurrent op race it —
        // defeating the serialization the gate exists for.
        await using var gate = await ModelOperationGate.AcquireAsync(_model, cancellationToken).ConfigureAwait(false);
        await _runner.UnloadAsync(_model, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

      try
      {
        await _runner.RemoveAsync(_model, force, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ModelInfo> InspectAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return _runner.InspectAsync(_model, cancellationToken);
    }

    /// <inheritdoc />
    public Task ConfigureAsync(ModelConfigureOptions options, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return _runner.ConfigureAsync(_model, options, cancellationToken);
    }

    /// <inheritdoc />
    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(hook);
      _stateHooks[state].Add(hook);
      _hooks[uniqueName ?? Guid.NewGuid().ToString()] = hook;
      return this;
    }

    /// <inheritdoc />
    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      if (uniqueName != null && _hooks.Remove(uniqueName, out var hook))
      {
        foreach (var list in _stateHooks.Values)
          list.Remove(hook);
      }

      return this;
    }

    /// <summary>
    /// Synchronously disposes the service. When the model is loaded and not kept
    /// running, this attempts to unload it. The unload is dispatched onto the thread
    /// pool (no captured <see cref="SynchronizationContext"/>) to avoid sync-over-async
    /// deadlocks on UI/ASP.NET contexts; prefer <see cref="DisposeAsync"/> for fully
    /// asynchronous unload semantics.
    /// </summary>
    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      // Run the async unload on the thread pool to escape any captured
      // SynchronizationContext and avoid the classic sync-over-async deadlock.
      // DisposeCoreAsync already swallows/logs unload failures.
      Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
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
      try
      {
        // _keepRunning controls only whether the model is unloaded — it must NOT
        // gate disposal of the owned runner (which may hold an inference connection,
        // X509 cert, HttpClient, etc.).
        if (!_keepRunning)
        {
          if (_state == ServiceRunningState.Running)
          {
            await StopCoreAsync().ConfigureAwait(false);
          }
          else if (_loadInitiated &&
                   _state != ServiceRunningState.Stopped &&
                   _state != ServiceRunningState.Removed)
          {
            // A load was attempted but we never reached Running (it faulted/cancelled
            // mid-load) — the model may still be resident. Best-effort unload so we
            // don't leak it; failures are swallowed by the surrounding catch. Serialize
            // on the per-model gate so this cleanup cannot race a concurrent op.
            await using var gate = await ModelOperationGate.AcquireAsync(_model).ConfigureAwait(false);
            await _runner.UnloadAsync(_model).ConfigureAwait(false);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "ModelService dispose unload failed for '{Model}'", _model);
      }
      finally
      {
        await _runner.DisposeAsync().ConfigureAwait(false);
      }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

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
