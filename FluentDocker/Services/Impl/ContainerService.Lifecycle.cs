using System;
using System.Collections.Generic;
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

    /// <inheritdoc />
    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(hook);
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
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

    /// <inheritdoc />
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

    internal void InvalidateInspectCache()
    {
      // Must serialize with ApplyInspectResultIfVersionCurrentAsync (ContainerService.Inspect.cs),
      // which checks _cacheVersion and writes _inspectCacheEntry under _stateLock: without the
      // lock, an invalidation landing between that check and write would be overwritten by the
      // stale inspect result. Monitor is reentrant, so UpdateStateCore invoking this while already
      // holding _stateLock is safe.
      lock (_stateLock)
      {
        Interlocked.Increment(ref _cacheVersion);
        _inspectCacheEntry = null;
      }
    }

    private async ValueTask DisposeCoreAsync()
    {
      if (_state == ServiceRunningState.Removed)
        return;

      var removeBudget = TimeSpan.FromMilliseconds(
          Math.Max(1, _disposeCleanupTimeout.TotalMilliseconds / 3));
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

      // Only skip the stop on the terminal Removed state. A cached "Stopped" may be stale
      // (restart policy / external `docker start`), and StopCoreAsync is daemon-idempotent
      // (IsAlreadyNotRunning maps to success) — mirroring StopCoreAsync's own SVC-MAJ-4 rule.
      if (_stopOnDispose && _state != ServiceRunningState.Removed)
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
        Interlocked.Increment(ref _disposeRemoveVersion);
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
        if (_deleteNamedVolumeOnDispose)
        {
          _logger.LogWarning(
              "ContainerService named volume cleanup skipped for '{Container}' because deleteOnDispose is false",
              _name);
        }

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
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);
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
            await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
            await RemoveNamedVolumesAsync(namedVolumeDriver, context, namedVolumes, cancellationToken)
                .ConfigureAwait(false);
            return;
          }

          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
          throw new DriverException(
              $"Failed to remove container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
        await RemoveNamedVolumesAsync(namedVolumeDriver, context, namedVolumes, cancellationToken)
            .ConfigureAwait(false);
      }
      catch
      {
        // If the container itself was already removed (terminal Removed reached), a later failure —
        // e.g. cancellation during the post-remove named-volume cleanup — must NOT downgrade the
        // terminal state back to Unknown (SVC-2).
        if (_state != ServiceRunningState.Removed)
          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
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
        catch (Exception ex) when (ex is not OperationCanceledException)
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

    // Pause fails "is already paused" when the container is already paused — idempotent success.
    private static bool IsAlreadyPaused(CommandResponse<Unit> response) =>
        response.Error?.Contains("is already paused", StringComparison.OrdinalIgnoreCase) == true;

    // SIGKILL (9) is the only signal a process cannot catch, block, or ignore, so it is guaranteed
    // to terminate the container. Every other signal may be handled, so post-kill state must be
    // inspected rather than assumed (SVC-MAJ-1).
    private static bool IsGuaranteedTerminalSignal(string signal)
    {
      if (string.IsNullOrEmpty(signal))
        return true; // default is SIGKILL
      var s = signal.Trim();
      if (s.StartsWith("SIG", StringComparison.OrdinalIgnoreCase))
        s = s[3..];
      return s.Equals("KILL", StringComparison.OrdinalIgnoreCase) || s == "9";
    }

    private async Task RunDisposeHooksWithoutRemovalAsync(CancellationToken cancellationToken)
    {
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
  }
}
