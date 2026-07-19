using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// <see cref="ModelService"/> disposal, state transitions and hook execution. Split from the
  /// main <see cref="ModelService"/> file purely to keep each source file within the repository's
  /// 500-line limit (SVC-6); behavior is identical.
  /// </summary>
  public sealed partial class ModelService
  {
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
      try
      {
        Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
      }
      GC.SuppressFinalize(this);
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
      }
      GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeCoreAsync()
    {
      Volatile.Write(ref _loadCancellationSignaled, 1);
      _loadCancellation.Cancel();
      try
      {
        // _keepRunning controls only whether the model is unloaded — it must NOT
        // gate disposal of the owned runner (which may hold an inference connection,
        // X509 cert, HttpClient, etc.).
        if (!_keepRunning)
        {
          using var cts = new CancellationTokenSource(_disposeCleanupTimeout);
          if (State == ServiceRunningState.Running)
          {
            await StopCoreAsync(cts.Token).ConfigureAwait(false);
          }
          else if (Volatile.Read(ref _loadAttempted) != 0 &&
                   State != ServiceRunningState.Stopped &&
                   State != ServiceRunningState.Removed)
          {
            // A load was attempted but we never reached Running (it faulted/cancelled
            // mid-load) — the model may still be resident. Best-effort unload so we
            // don't leak it; failures are swallowed by the surrounding catch.
            await _runner.UnloadAsync(_model, cts.Token).ConfigureAwait(false);
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
          if (_activeLoadTask is { } lt)
            await lt.WaitAsync(_disposeCleanupTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Model load did not settle before runner disposal");
        }

        try
        {
          await _runner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ModelService runner disposal failed for '{Model}'", _model);
        }
        _loadCancellation.Dispose();
      }
    }

    // ponytail: key on _disposeCompleted (dispose finished), not _disposed (dispose started), so
    // user lifecycle hooks executing during DisposeCoreAsync can still call public members.
    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);

    private void UpdateState(ServiceRunningState newState)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        // Suppress post-dispose state changes/events, matching ContainerService (SVC-3): hooks
        // running during DisposeCoreAsync must not resurrect state or fire StateChange afterwards.
        if (Volatile.Read(ref _disposeCompleted) != 0)
          return;

        if ((ServiceRunningState)Volatile.Read(ref _state) == newState)
          return;

        Volatile.Write(ref _state, (int)newState);
        stateChange = StateChange;
        if (stateChange == null)
          return;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "ModelService");
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state, CancellationToken cancellationToken = default)
    {
      // Suppress hook execution once dispose has completed, matching ContainerService (SVC-3).
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

      // Hook execution order is unspecified (concurrent snapshot); do not rely on
      // registration order.
      cancellationToken.ThrowIfCancellationRequested();
      foreach (var entry in _hooks.Values)
      {
        if (entry.State != state)
          continue;

        try
        {
          // WaitAsync bounds how long dispose waits; hooks have no CancellationToken, so their bodies may continue.
          await entry.Hook(this).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ModelService hook execution failed");
        }
      }
    }
  }
}
