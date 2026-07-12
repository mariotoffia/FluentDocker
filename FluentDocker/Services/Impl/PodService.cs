using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <inheritdoc />
  /// <remarks>
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public class PodService : IPodService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;

    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<PodService> _logger;
    private readonly string _driverId;
    private readonly string _podName;
    private readonly string _podId;
    private readonly bool _removeOnDispose;
    private readonly TimeSpan _disposeCleanupTimeout;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _stateLock = new();
    private volatile ServiceRunningState _state = ServiceRunningState.Stopped;

    /// <summary>
    /// Creates a Podman pod service.
    /// </summary>
    /// <param name="kernel">Kernel used to resolve Podman pod driver ports.</param>
    /// <param name="driverId">Driver id registered in the kernel.</param>
    /// <param name="podId">Pod id used for driver operations.</param>
    /// <param name="podName">Pod name used for driver operations; null falls back to <paramref name="podId"/>.</param>
    /// <param name="removeOnDispose">When true, dispose removes the owned pod.</param>
    /// <param name="disposeCleanupTimeout">Maximum best-effort remove time during dispose.</param>
    public PodService(
        FluentDockerKernel kernel, string driverId,
        string podId, string podName, bool removeOnDispose = false,
        TimeSpan? disposeCleanupTimeout = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(podId);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<PodService>();
      _driverId = driverId;
      _podId = podId;
      _podName = podName ?? podId;
      _removeOnDispose = removeOnDispose;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
    }

    /// <inheritdoc />
    public string Name => _podName;

    /// <inheritdoc />
    public string Id => _podId;

    /// <inheritdoc />
    public ServiceRunningState State => _state;

    /// <inheritdoc />
    public FluentDockerKernel Kernel => _kernel;

    /// <inheritdoc />
    public string DriverId => _driverId;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    /// <inheritdoc />
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      // Guard the terminal Removed state (like the sibling ContainerService/ComposeService starts):
      // without it Removed -> Starting fires hooks against a gone pod and the driver's "no such pod"
      // catch overwrites the terminal state with Unknown (SVC-MAJ-2).
      if (State is ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot start a removed pod.");
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Starting);
        await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

        var response = await driver.StartPodAsync(context, _podName, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to start pod '{_podName}': {response.Error}",
              ResolveErrorCode(response.ErrorCode, ErrorCodes.Pod.StartFailed),
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Running);
        await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      await StopAsync(timeoutSeconds: 10, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the pod and lets Podman wait up to <paramref name="timeoutSeconds"/> seconds.
    /// The inherited overload uses the 10-second default.
    /// </summary>
    /// <param name="timeoutSeconds">Seconds to wait before Podman kills pod containers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(int timeoutSeconds, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (timeoutSeconds < 0)
        throw new ArgumentOutOfRangeException(
            nameof(timeoutSeconds), timeoutSeconds, "Pod stop timeout must be non-negative.");
      // A fresh pod starts in Stopped, so only Removed is a terminal state to guard here — mirrors
      // RemoveAsync's guard so a stop can't resurrect a removed pod (Removed -> Stopping -> Stopped).
      if (State is ServiceRunningState.Removed)
        return;
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Stopping);
        await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

        var response = await driver.StopPodAsync(context, _podName, timeoutSeconds, cancellationToken).ConfigureAwait(false);
        if (!response.Success && !IsPodAlreadyStopped(response))
        {
          throw new DriverException(
              $"Failed to stop pod '{_podName}': {response.Error}",
              ResolveErrorCode(response.ErrorCode, ErrorCodes.Pod.StopFailed),
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Stopped);
        await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    /// <summary>Pausing a pod as a whole is not exposed by the builder surface.</summary>
    /// <exception cref="FluentDockerNotSupportedException">Always thrown.</exception>
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Pods cannot be paused via builder");
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        bool force = false, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (State == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Removing);
        await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

        var response = await driver.RemovePodAsync(
            context, _podName, force, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
          if (IsPodAlreadyGone(response))
          {
            UpdateState(ServiceRunningState.Removed);
            await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
            return;
          }

          throw new DriverException(
              $"Failed to remove pod '{_podName}': {response.Error}",
              ResolveErrorCode(response.ErrorCode, ErrorCodes.Pod.RemoveFailed),
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Removed);
        await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    /// <inheritdoc />
    public IServiceAsync AddHook(
        ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    /// <inheritdoc />
    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private int _disposed;
    private int _disposeCompleted;

    /// <inheritdoc />
    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        // Dispatched to the thread pool to avoid sync-over-async deadlocks.
        Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        await DisposeCoreAsync().ConfigureAwait(false);
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    private async ValueTask DisposeCoreAsync()
    {
      if (!_removeOnDispose)
        return;

      using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);
      var removeTask = RemoveAsync(force: true, cleanupCts.Token);
      try
      {
        await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "PodService DisposeAsync failed");
        ObserveAbandonedCleanup(removeTask);
      }
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);

    private void UpdateState(ServiceRunningState newState)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
          return;

        _state = newState;
        stateChange = StateChange;
        if (stateChange == null)
          return;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "PodService");
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

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
          _logger.LogError(ex, "PodService hook execution failed");
        }
      }
    }

    private static string ResolveErrorCode(string errorCode, string fallback) =>
        string.IsNullOrWhiteSpace(errorCode) || errorCode == ErrorCodes.General.Unknown ? fallback : errorCode;

    // Podman driver maps "no such pod" removes to Pod.NotFound; keep the phrase fallback for
    // older/ad-hoc drivers without broad "not found" masking.
    private static bool IsPodAlreadyGone(CommandResponse<Unit> response) =>
        response.ErrorCode == ErrorCodes.Pod.NotFound ||
        response.Error?.Contains("no such pod", StringComparison.OrdinalIgnoreCase) == true;

    // ponytail: tolerate Podman wording for redundant stops without broad "not found" masking.
    private static bool IsPodAlreadyStopped(CommandResponse<Unit> response) =>
        IsPodAlreadyGone(response) ||
        response.Error?.Contains("not running", StringComparison.OrdinalIgnoreCase) == true ||
        response.Error?.Contains("already stopped", StringComparison.OrdinalIgnoreCase) == true;
  }
}
