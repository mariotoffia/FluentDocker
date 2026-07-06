using System;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _startSync = new();
    private int _state = (int)ServiceRunningState.Unknown;
    // Start-once gate. Reset after hard failures and successful unload/remove so retry and
    // reload semantics match the public lifecycle.
    private int _loadInitiated;
    // Persistent marker for dispose best-effort unload after a load reached the runner.
    private int _loadAttempted;
    private int _disposed;
    private Task _loadTask;

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

    }

    /// <inheritdoc />
    public string Name => _model.ToString();

    /// <inheritdoc />
    public ServiceRunningState State => (ServiceRunningState)Volatile.Read(ref _state);

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
      cancellationToken.ThrowIfCancellationRequested();
      Task loadTask;
      TaskCompletionSource<bool> completion = null;
      lock (_startSync)
      {
        if (_loadInitiated != 0)
        {
          loadTask = _loadTask;
        }
        else
        {
          _loadInitiated = 1;
          completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
          _loadTask = completion.Task;
          loadTask = _loadTask;
        }
      }

      if (completion != null)
        _ = DriveSharedLoadAsync(completion);

      // Winner and losers alike observe the SINGLE shared load to completion/failure. The load
      // runs under CancellationToken.None (see DriveSharedLoadAsync), so it is deliberately NOT
      // abandoned when an individual caller's token fires: one load serves every concurrent
      // caller, and the per-model gate must stay held until the driver's load actually returns
      // (StartAsync_HoldsGateForFullLoad_NotReleasedEarlyOnCancel). A pre-cancelled token still
      // fails fast via the guard above; a bounded wait otherwise relies on the driver timeout.
      await loadTask.ConfigureAwait(false);
    }

    // Drives the one elected load to completion and publishes its outcome to every waiter.
    // ponytail: the load runs under CancellationToken.None so a single caller cancelling cannot
    // fail it for the others; every caller awaits the shared outcome to completion (the per-model
    // gate is held for the full load — see StartAsync). Give the load a caller-driven cancel only
    // if a concrete need for a bounded, abandonable wait ever appears.
    private async Task DriveSharedLoadAsync(TaskCompletionSource<bool> completion)
    {
      try
      {
        await StartCoreAsync(CancellationToken.None).ConfigureAwait(false);
        completion.SetResult(true);
      }
      catch (Exception ex)
      {
        // Self-heal the start-once gate on ANY failure (load or hook) so a later StartAsync can
        // retry instead of the service wedging permanently.
        // Matches the stop/remove reset idiom (Volatile.Write); the winning StartAsync reads the
        // gate under _startSync, whose Monitor barrier observes this release.
        Volatile.Write(ref _loadInitiated, 0);
        completion.SetException(ex);
      }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
      UpdateState(ServiceRunningState.Starting);
      await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

      try
      {
        Volatile.Write(ref _loadAttempted, 1);
        await _runner.LoadAsync(_model, _runOptions, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        // The start-once gate is reset by DriveSharedLoadAsync on any failure.
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
      throw new FluentDockerNotSupportedException("Models cannot be paused; use Stop (unload) instead.");
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
        await _runner.UnloadAsync(_model, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }

      Volatile.Write(ref _loadInitiated, 0);
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

      Volatile.Write(ref _loadInitiated, 0);
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
      _hooks[uniqueName ?? Guid.NewGuid().ToString()] = (state, hook);
      return this;
    }

    /// <inheritdoc />
    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      if (uniqueName != null)
        _hooks.TryRemove(uniqueName, out _);
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
          if (State == ServiceRunningState.Running)
          {
            await StopCoreAsync().ConfigureAwait(false);
          }
          else if (Volatile.Read(ref _loadAttempted) != 0 &&
                   State != ServiceRunningState.Stopped &&
                   State != ServiceRunningState.Removed)
          {
            // A load was attempted but we never reached Running (it faulted/cancelled
            // mid-load) — the model may still be resident. Best-effort unload so we
            // don't leak it; failures are swallowed by the surrounding catch.
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
        try
        {
          await _runner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ModelService runner disposal failed for '{Model}'", _model);
        }
      }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private void UpdateState(ServiceRunningState newState)
    {
      if ((ServiceRunningState)Volatile.Read(ref _state) == newState)
        return;

      Volatile.Write(ref _state, (int)newState);
      var stateChange = StateChange;
      if (stateChange == null)
        return;

      var args = new StateChangeEventArgs(this, newState);
      foreach (ServiceDelegates.StateChange handler in stateChange.GetInvocationList())
      {
        try
        {
          handler(this, args);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ModelService state change handler failed");
        }
      }
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      // Hook execution order is unspecified (concurrent snapshot); do not rely on
      // registration order.
      foreach (var entry in _hooks.Values)
      {
        if (entry.State != state)
          continue;

        try
        {
          await entry.Hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ModelService hook execution failed");
        }
      }
    }
  }
}
