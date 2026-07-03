using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <inheritdoc />
  public partial class ContainerService
  {
    private int _disposed;
    private int _disposeCompleted;

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
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

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      try
      {
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

    internal void InvalidateInspectCache()
    {
      Interlocked.Increment(ref _cacheVersion);
      _inspectCacheEntry = null;
    }

    private async ValueTask DisposeCoreAsync()
    {
      if (_state == ServiceRunningState.Removed)
        return;

      using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);

      var preStopHookTask = ExecuteLifecycleHooksAsync(
          ServiceRunningState.Removing,
          cleanupCts.Token,
          LifecycleHookType.Execute,
          includeOnly: true);
      try
      {
        await preStopHookTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "ContainerService execute-on-dispose hook failed");
        ObserveAbandonedCleanup(preStopHookTask);
      }

      if (_stopOnDispose && _state != ServiceRunningState.Stopped && _state != ServiceRunningState.Removed)
      {
        var stopTask = StopAsync(cleanupCts.Token);
        try
        {
          await stopTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ContainerService stop on dispose failed");
          ObserveAbandonedCleanup(stopTask);
        }
      }

      if (_deleteOnDispose)
      {
        var removeTask = RemoveCoreAsync(
            force: true, skipExecuteLifecycleHooks: true, removeVolumesOverride: null, cleanupCts.Token);
        try
        {
          await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ContainerService remove on dispose failed");
          ObserveAbandonedCleanup(removeTask);
        }
      }
      else
      {
        var hookTask = RunDisposeHooksWithoutRemovalAsync(cleanupCts.Token);
        try
        {
          await hookTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ContainerService dispose hooks failed");
          ObserveAbandonedCleanup(hookTask);
        }
      }
    }

    private async Task RemoveCoreAsync(
        bool force,
        bool skipExecuteLifecycleHooks,
        bool? removeVolumesOverride,
        CancellationToken cancellationToken)
    {
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);
      await ExecuteLifecycleHooksAsync(
          ServiceRunningState.Removing,
          cancellationToken,
          LifecycleHookType.Execute,
          includeOnly: false,
          skipType: skipExecuteLifecycleHooks).ConfigureAwait(false);

      var removeVolumes = removeVolumesOverride ?? (_deleteVolumeOnDispose || _deleteNamedVolumeOnDispose);
      var response = await driver.RemoveAsync(
          context, _containerId, force, removeVolumes, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        UpdateState(ServiceRunningState.Unknown);
        throw new DriverException(
            $"Failed to remove container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    private async Task RunDisposeHooksWithoutRemovalAsync(CancellationToken cancellationToken)
    {
      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);
      await ExecuteLifecycleHooksAsync(
          ServiceRunningState.Removing,
          cancellationToken,
          LifecycleHookType.Execute,
          includeOnly: false,
          skipType: true).ConfigureAwait(false);
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void UpdateState(ServiceRunningState newState) => UpdateStateCore(newState, invalidateInspectCache: true);

    private void UpdateStateFromInspect(ServiceRunningState newState) =>
        UpdateStateCore(newState, invalidateInspectCache: false);

    private void UpdateStateCore(ServiceRunningState newState, bool invalidateInspectCache)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

      var oldState = _state;
      if (oldState == newState)
        return;

      _state = newState;
      if (invalidateInspectCache)
        InvalidateInspectCache();

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
          _logger.LogError(ex, "ContainerService state change handler failed");
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
          _logger.LogError(ex, "ContainerService hook execution failed");
        }
      }
    }

    private async Task ExecuteLifecycleHooksAsync(
        ServiceRunningState state,
        CancellationToken cancellationToken,
        LifecycleHookType? type = null,
        bool includeOnly = false,
        bool skipType = false)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

      foreach (var hook in _lifecycleHooks)
      {
        if (hook.TriggerState != state ||
            (type.HasValue && includeOnly && hook.Type != type.Value) ||
            (type.HasValue && skipType && hook.Type == type.Value))
          continue;

        try
        {
          switch (hook.Type)
          {
            case LifecycleHookType.CopyTo:
              if (File.Exists(hook.HostPath) || Directory.Exists(hook.HostPath))
                await CopyToAsync(hook.HostPath, hook.ContainerPath, cancellationToken).ConfigureAwait(false);
              break;

            case LifecycleHookType.CopyFrom:
              await CopyFromToPathAsync(hook.ContainerPath, hook.HostPath, cancellationToken).ConfigureAwait(false);
              break;

            case LifecycleHookType.Export:
              await ExecuteExportHookAsync(hook, cancellationToken).ConfigureAwait(false);
              break;

            case LifecycleHookType.Execute:
              if (hook.Command != null)
                await ExecuteAsync(hook.Command, cancellationToken).ConfigureAwait(false);
              break;
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Lifecycle hook failed");
          if (hook.Type == LifecycleHookType.Export && hook.Explode)
            throw;
        }
      }
    }

    private async Task ExecuteExportHookAsync(LifecycleHook hook, CancellationToken cancellationToken)
    {
      if (hook.Condition != null && !hook.Condition(this))
        return;

      var exportData = await ExportAsync(cancellationToken).ConfigureAwait(false);
      var exportDir = Path.GetDirectoryName(hook.HostPath);
      if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
        Directory.CreateDirectory(exportDir);

      if (hook.Explode)
      {
        Directory.CreateDirectory(hook.HostPath);
        using var stream = new MemoryStream(exportData);
        TarFile.ExtractToDirectory(stream, hook.HostPath, overwriteFiles: true);
      }
      else
      {
        await File.WriteAllBytesAsync(hook.HostPath, exportData, cancellationToken).ConfigureAwait(false);
      }
    }

    private static ServiceRunningState ParseState(string state)
    {
      return state?.ToLowerInvariant() switch
      {
        "running" => ServiceRunningState.Running,
        "paused" => ServiceRunningState.Paused,
        "exited" => ServiceRunningState.Stopped,
        "created" => ServiceRunningState.Starting,
        _ => ServiceRunningState.Unknown
      };
    }
  }
}
