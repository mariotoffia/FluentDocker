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
  /// <remarks>
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public sealed partial class ModelService : IModelService, IServiceCapabilities
  {
    // ponytail: one generous wall-clock ceiling; expose finer policy only after real workloads need it.
    private static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromHours(2);
    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<ModelService> _logger;
    private readonly string _driverId;
    private readonly ModelReference _model;
    private readonly IModelRunner _runner;
    private readonly ModelRunOptions? _runOptions;
    private readonly bool _keepRunning;
    private readonly TimeSpan _disposeCleanupTimeout;
    private readonly TimeSpan _loadTimeout;
    private readonly CancellationTokenSource _loadCancellation;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _stateLock = new();
    private readonly object _startSync = new();
    private int _state = (int)ServiceRunningState.Unknown;
    // Start-once gate. Reset after hard failures and successful unload/remove so retry and
    // reload semantics match the public lifecycle.
    private int _loadInitiated;
    // Persistent marker for dispose best-effort unload after a load reached the runner.
    private int _loadAttempted;
    private int _disposed;
    // ponytail: dispose-completion flag — ThrowIfDisposed keys on this (dispose finished),
    // not _disposed (dispose started), so Stopping/Stopped hooks running during
    // DisposeCoreAsync can still call public members without ObjectDisposedException
    // (mirrors ContainerService).
    private int _disposeCompleted;
    private int _loadCancellationSignaled;
    private Task _loadTask = null!;
    private Task? _activeLoadTask;

    /// <summary>Initializes the model service.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="driverId">The driver id.</param>
    /// <param name="model">The model this service manages.</param>
    /// <param name="runner">The runner bound to this model.</param>
    /// <param name="runOptions">Options for loading (StartAsync).</param>
    /// <param name="keepRunning">When true, the model is NOT unloaded on dispose.</param>
    /// <param name="disposeCleanupTimeout">Maximum best-effort unload time during dispose.</param>
    /// <param name="loadTimeout">Maximum wall-clock time for the shared StartAsync load.</param>
    public ModelService(FluentDockerKernel kernel, string driverId, ModelReference model,
        IModelRunner runner, ModelRunOptions? runOptions = null, bool keepRunning = false,
        TimeSpan? disposeCleanupTimeout = null, TimeSpan? loadTimeout = null)
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
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
      _loadTimeout = loadTimeout ?? DefaultLoadTimeout;
      if (_loadTimeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(loadTimeout), "Model load timeout must be positive.");
      _loadCancellation = new CancellationTokenSource();

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

    /// <inheritdoc />
    bool IServiceCapabilities.CanHook => true;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design (mirrors IServiceAsync)
    /// <inheritdoc />
    public event ServiceDelegates.StateChange StateChange = null!;
#pragma warning restore CA1710

    /// <inheritdoc />
    /// <remarks>
    /// The first caller drives one shared load and concurrent callers await that same load.
    /// Cancelling a caller's token abandons only that caller's wait, not the shared load; the
    /// per-model gate remains held until the load actually completes. A load timeout or dispose
    /// cancels the service-owned load token and resets the gate so the driver can reap
    /// cancellable work before a retry.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      cancellationToken.ThrowIfCancellationRequested();
      // Guard the terminal Removed state (like the sibling ContainerService/ComposeService/PodService
      // starts): RemoveAsync resets the start-once gate (_loadInitiated), so without this guard a
      // removed model could be silently re-loaded/resurrected (SVC-1).
      if (State == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot start a removed model.");
      Task loadTask;
      TaskCompletionSource<bool>? completion = null;
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
          ObserveFault(_loadTask);
          loadTask = _loadTask;
        }
      }

      if (completion != null)
        ObserveFault(DriveSharedLoadAsync(completion));

      // Winner and losers alike observe the SINGLE shared load to completion/failure. Individual
      // caller cancellation abandons only that caller's wait; the shared load has its own generous
      // timeout/dispose token so the gate can self-heal if the driver never returns.
      await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
    }

    // Drives the one elected load to completion and publishes its outcome to every waiter.
    private async Task DriveSharedLoadAsync(TaskCompletionSource<bool> completion)
    {
      CancellationTokenSource? loadCts = null;
      Task? loadTask = null;
      try
      {
        loadCts = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation.Token);
        loadCts.CancelAfter(_loadTimeout);
        loadTask = _activeLoadTask = StartCoreAsync(loadCts.Token);
        await loadTask.WaitAsync(_loadTimeout, _loadCancellation.Token).ConfigureAwait(false);
        completion.TrySetResult(true);
      }
      catch (OperationCanceledException) when (Volatile.Read(ref _loadCancellationSignaled) != 0)
      {
        loadCts?.Cancel();
        FaultSharedLoad(completion, new ObjectDisposedException(nameof(ModelService)));
      }
      catch (OperationCanceledException ex) when (!_loadCancellation.IsCancellationRequested && loadCts?.IsCancellationRequested == true)
      {
        FaultSharedLoad(completion, LoadTimedOut(ex));
      }
      catch (TimeoutException ex)
      {
        loadCts?.Cancel();
        FaultSharedLoad(completion, LoadTimedOut(ex));
      }
      catch (Exception ex)
      {
        FaultSharedLoad(completion, ex);
      }
      finally
      {
        if (loadCts != null)
        {
          if (loadTask == null || loadTask.IsCompleted)
            loadCts.Dispose();
          else
            _ = DisposeWhenLoadCompletesAsync(loadTask, loadCts);
        }
      }
    }

    private void FaultSharedLoad(TaskCompletionSource<bool> completion, Exception ex)
    {
      // Self-heal the start-once gate on ANY failure (load, hook, timeout or dispose) so a later
      // StartAsync can retry instead of the service wedging permanently.
      completion.TrySetException(ex);
      Volatile.Write(ref _loadInitiated, 0);
      UpdateState(ServiceRunningState.Unknown);
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(static faulted => _ = faulted.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private TimeoutException LoadTimedOut(Exception inner) =>
        new($"The model load exceeded the configured model load timeout of {_loadTimeout}.", inner);

    private async Task DisposeWhenLoadCompletesAsync(Task loadTask, CancellationTokenSource loadCts)
    {
      try
      {
        await loadTask.WaitAsync(_loadTimeout).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Model load did not complete before timeout/cancellation");
      }
      finally
      {
        loadCts.Dispose();
      }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
      ThrowIfDisposed();
      // Re-check the terminal Removed state after the gate election, mirroring the sibling
      // services: a RemoveAsync could have landed between the StartAsync guard and here (SVC-1).
      if (State == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot start a removed model.");

      UpdateState(ServiceRunningState.Starting);
      await ExecuteHooksAsync(ServiceRunningState.Starting, cancellationToken).ConfigureAwait(false);

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

      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running, cancellationToken).ConfigureAwait(false);
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
      cancellationToken.ThrowIfCancellationRequested();
      if (State is ServiceRunningState.Stopped or ServiceRunningState.Removed)
        return;

      UpdateState(ServiceRunningState.Stopping);
      await ExecuteHooksAsync(ServiceRunningState.Stopping, cancellationToken).ConfigureAwait(false);

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
      await ExecuteHooksAsync(ServiceRunningState.Stopped, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      cancellationToken.ThrowIfCancellationRequested();
      if (State == ServiceRunningState.Removed)
        return;

      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing, cancellationToken).ConfigureAwait(false);

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
      await ExecuteHooksAsync(ServiceRunningState.Removed, cancellationToken).ConfigureAwait(false);
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
    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string? uniqueName = null)
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
  }
}
