using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Text.RegularExpressions;
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
    private static readonly Regex AnonymousVolumeNameRegex = new(
        "^[0-9a-f]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

      var removeBudget = TimeSpan.FromMilliseconds(Math.Min(
          5_000,
          Math.Max(1, _disposeCleanupTimeout.TotalMilliseconds / 3)));
      var preRemoveBudget = _disposeCleanupTimeout > removeBudget
          ? _disposeCleanupTimeout - removeBudget
          : TimeSpan.FromMilliseconds(1);
      using var cleanupCts = new CancellationTokenSource(preRemoveBudget);

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
        var stopTask = StopCoreAsync(throwIfDisposed: false, cleanupCts.Token);
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
        using var removeCts = new CancellationTokenSource(removeBudget);
        var removeTask = RemoveCoreAsync(
            force: true, skipExecuteLifecycleHooks: true, removeVolumesOverride: null,
            throwIfDisposed: false, removeCts.Token);
        try
        {
          await removeTask.WaitAsync(removeCts.Token).ConfigureAwait(false);
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
        bool throwIfDisposed,
        CancellationToken cancellationToken)
    {
      if (throwIfDisposed)
        ThrowIfDisposed();
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

      IVolumeDriver namedVolumeDriver = null;
      var namedVolumes = Array.Empty<string>();
      if (_deleteNamedVolumeOnDispose)
      {
        if (_kernel.TrySysCtl<IVolumeDriver>(_driverId, out namedVolumeDriver))
        {
          namedVolumes = await InspectNamedVolumesAsync(driver, context, cancellationToken)
              .ConfigureAwait(false);
        }
        else
        {
          _logger.LogWarning(
              "ContainerService named volume cleanup skipped for '{Container}' because IVolumeDriver is unavailable",
              _name);
        }
      }

      var removeVolumes = removeVolumesOverride ?? _deleteVolumeOnDispose;
      var response = await driver.RemoveAsync(
          context, _containerId, force, removeVolumes, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        if (IsContainerAlreadyGone(response))
        {
          UpdateState(ServiceRunningState.Removed);
          await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
          await RemoveNamedVolumesAsync(namedVolumeDriver, context, namedVolumes, cancellationToken)
              .ConfigureAwait(false);
          return;
        }

        UpdateState(ServiceRunningState.Unknown);
        throw new DriverException(
            $"Failed to remove container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
      await RemoveNamedVolumesAsync(namedVolumeDriver, context, namedVolumes, cancellationToken)
          .ConfigureAwait(false);
    }

    private async Task<string[]> InspectNamedVolumesAsync(
        IContainerDriver driver,
        DriverContext context,
        CancellationToken cancellationToken)
    {
      var response = await driver.InspectAsync(context, _containerId, cancellationToken)
          .ConfigureAwait(false);
      if (!response.Success)
      {
        _logger.LogWarning(
            "ContainerService named volume cleanup inspect failed for '{Container}': {Error}",
            _name,
            response.Error);
        return [];
      }

      var mounts = response.Data?.Mounts;
      if (mounts == null || mounts.Length == 0)
        return [];

      var names = new List<string>();
      foreach (var mount in mounts)
      {
        // ponytail: Mount lacks Type; map inspect Type:"volume" before trusting Source for bind-vs-volume.
        var name = mount?.Name;
        if (string.IsNullOrWhiteSpace(name) || AnonymousVolumeNameRegex.IsMatch(name))
          continue;

        if (!names.Contains(name))
          names.Add(name);
      }

      return [.. names];
    }

    private async Task RemoveNamedVolumesAsync(
        IVolumeDriver driver,
        DriverContext context,
        string[] volumeNames,
        CancellationToken cancellationToken)
    {
      if (driver == null || volumeNames.Length == 0)
        return;

      foreach (var volumeName in volumeNames)
      {
        try
        {
          var response = await driver.RemoveAsync(context, volumeName, false, cancellationToken)
              .ConfigureAwait(false);
          if (!response.Success)
          {
            _logger.LogWarning(
                "ContainerService named volume '{Volume}' cleanup failed: {Error}",
                volumeName,
                response.Error);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(
              ex,
              "ContainerService named volume '{Volume}' cleanup failed",
              volumeName);
        }
      }
    }

    private static bool IsContainerAlreadyGone<T>(CommandResponse<T> response)
    {
      if (response.ErrorCode == ErrorCodes.Container.NotFound)
        return true;

      return response.Error?.Contains("no such container", StringComparison.OrdinalIgnoreCase) == true ||
          response.Error?.Contains("no container with", StringComparison.OrdinalIgnoreCase) == true;
    }

    // Stop/Kill are not daemon-idempotent: the driver fails ("is not running" / "no such container")
    // when the container is already stopped or gone. Those outcomes satisfy the caller's intent, so
    // treat them as success instead of throwing and corrupting state on a redundant/retried call.
    private static bool IsAlreadyNotRunning(CommandResponse<Unit> response) =>
        IsContainerAlreadyGone(response) ||
        response.Error?.Contains("is not running", StringComparison.OrdinalIgnoreCase) == true;

    // Unpause fails "is not paused" when the container is already running — idempotent success.
    private static bool IsAlreadyNotPaused(CommandResponse<Unit> response) =>
        response.Error?.Contains("is not paused", StringComparison.OrdinalIgnoreCase) == true;

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

    private void UpdateStateCore(ServiceRunningState newState, bool invalidateInspectCache)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0)
          return;

        var oldState = _state;
        if (oldState == newState)
          return;

        _state = newState;
        if (invalidateInspectCache)
          InvalidateInspectCache();

        stateChange = StateChange;
        if (stateChange == null)
          return;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "ContainerService");
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
                await CopyToCoreAsync(
                    hook.HostPath, hook.ContainerPath, throwIfDisposed: false, cancellationToken)
                    .ConfigureAwait(false);
              else
                // ponytail: warn rather than throw to preserve existing no-op lifecycle hook behavior.
                _logger.LogWarning(
                    "Skipping CopyTo lifecycle hook for missing host path {HostPath} on container {ContainerId}",
                    hook.HostPath,
                    _containerId);
              break;

            case LifecycleHookType.CopyFrom:
              await CopyFromToPathCoreAsync(
                  hook.ContainerPath, hook.HostPath, throwIfDisposed: false, cancellationToken)
                  .ConfigureAwait(false);
              break;

            case LifecycleHookType.Export:
              await ExecuteExportHookAsync(hook, cancellationToken).ConfigureAwait(false);
              break;

            case LifecycleHookType.Execute:
              if (hook.Command != null)
                await ExecuteDetailedCoreAsync(
                    hook.Command, throwIfDisposed: false, cancellationToken).ConfigureAwait(false);
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

      var exportData = await ExportCoreAsync(throwIfDisposed: false, cancellationToken).ConfigureAwait(false);
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
        "stopped" => ServiceRunningState.Stopped,
        "created" => ServiceRunningState.Starting,
        "restarting" => ServiceRunningState.Starting,
        "stopping" => ServiceRunningState.Stopping,
        "removing" => ServiceRunningState.Removing,
        "dead" => ServiceRunningState.Stopped,
        _ => ServiceRunningState.Unknown
      };
    }
  }
}
