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

    public string Name => _podName;
    public string Id => _podId;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Stopping);
        await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

        var response = await driver.StopPodAsync(context, _podName, 10, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
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

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      throw new FluentDockerNotSupportedException("Pods cannot be paused via builder");
    }

    public async Task RemoveAsync(
        bool force = false, CancellationToken cancellationToken = default)
    {
      if (State == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

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

        UpdateState(ServiceRunningState.Unknown);
        throw new DriverException(
            $"Failed to remove pod '{_podName}': {response.Error}",
            ResolveErrorCode(response.ErrorCode, ErrorCodes.Pod.RemoveFailed),
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    public IServiceAsync AddHook(
        ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private int _disposed;
    private int _disposeCompleted;

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

    private void UpdateState(ServiceRunningState newState)
    {
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
          return;

        _state = newState;
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
            _logger.LogError(ex, "PodService state change handler failed");
          }
        }
      }
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

    // Pods are Podman-only and Podman sets the typed Pod.NotFound (plus the "no such pod" phrase),
    // so no bare "not found" fallback is needed — dropping it avoids masking unrelated failures.
    private static bool IsPodAlreadyGone(CommandResponse<Unit> response) =>
        response.ErrorCode == ErrorCodes.Pod.NotFound ||
        response.Error?.Contains("no such pod", StringComparison.OrdinalIgnoreCase) == true;
  }
}
