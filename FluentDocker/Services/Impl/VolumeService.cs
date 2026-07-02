using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Volume service implementation using kernel and driver.
  /// </summary>
  public class VolumeService : IVolumeService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => false;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;

    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<VolumeService> _logger;
    private readonly string _driverId;
    private readonly string _volumeName;
    private readonly string _driver;
    private readonly bool _removeOnDispose;
    private readonly TimeSpan _disposeCleanupTimeout;
    private readonly Dictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private ServiceRunningState _state = ServiceRunningState.Running;

    public VolumeService(
        FluentDockerKernel kernel,
        string driverId,
        string volumeName,
        string driver,
        bool removeOnDispose = false,
        TimeSpan? disposeCleanupTimeout = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(volumeName);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<VolumeService>();
      _driverId = driverId;
      _volumeName = volumeName;
      _driver = driver ?? "local";
      _removeOnDispose = removeOnDispose;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
    }

    public string Name => _volumeName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string VolumeName => _volumeName;
    public string Driver => _driver;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task<Volume> InspectAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _volumeName, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to inspect volume '{_volumeName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Volumes cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Volumes cannot be stopped, use RemoveAsync instead");
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.RemoveAsync(context, _volumeName, force, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove volume '{_volumeName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.Remove(uniqueName);
      return this;
    }

    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      // Dispatched to the thread pool to avoid sync-over-async deadlocks.
      Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      await DisposeCoreAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
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
        _logger.LogWarning(ex, "VolumeService DisposeAsync failed");
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
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      // Snapshot: a firing hook may add or remove hooks, which would invalidate a live enumerator.
      foreach (var entry in new List<(ServiceRunningState State, Func<IServiceAsync, Task> Hook)>(_hooks.Values))
      {
        if (entry.State != state)
          continue;

        try
        {
          await entry.Hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "VolumeService hook execution failed");
        }
      }
    }
  }
}
