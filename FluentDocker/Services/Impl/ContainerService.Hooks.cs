using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Container service — client-side state transitions, hook execution and inspect-state parsing.
  /// Split from <see cref="ContainerService"/> / <c>ContainerService.Lifecycle.cs</c> purely to keep
  /// each source file within the repository's 500-line limit (SVC-6); behavior is identical.
  /// </summary>
  public partial class ContainerService
  {
    private bool UpdateState(ServiceRunningState newState) => UpdateStateCore(newState, invalidateInspectCache: true);

    private async Task UpdateStateAndExecuteHooksAsync(ServiceRunningState newState)
    {
      if (UpdateState(newState))
        await ExecuteHooksAsync(newState).ConfigureAwait(false);
    }

    private bool UpdateStateCore(ServiceRunningState newState, bool invalidateInspectCache)
    {
      ServiceDelegates.StateChange stateChange = null;
      StateChangeEventArgs args = null;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0)
          return false;

        var oldState = _state;
        if (oldState == newState)
          return false;

        _state = newState;
        if (invalidateInspectCache)
          InvalidateInspectCache();

        stateChange = StateChange;
        args = stateChange == null ? null : new StateChangeEventArgs(this, newState);
      }

      if (stateChange != null)
        StateChangeNotifier.Invoke(stateChange, args, _logger, "ContainerService");
      return true;
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

    internal static ServiceRunningState ParseState(string state)
    {
      return state?.ToLowerInvariant() switch
      {
        "running" => ServiceRunningState.Running,
        "paused" => ServiceRunningState.Paused,
        "exited" => ServiceRunningState.Stopped,
        "stopped" => ServiceRunningState.Stopped,
        "created" => ServiceRunningState.Created,
        "restarting" => ServiceRunningState.Starting,
        "stopping" => ServiceRunningState.Stopping,
        "removing" => ServiceRunningState.Removing,
        "dead" => ServiceRunningState.Stopped,
        _ => ServiceRunningState.Unknown
      };
    }
  }
}
