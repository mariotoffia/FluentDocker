using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  public abstract partial class ResourceBase
  {
    private static readonly Action<ILogger, Exception> LateProvisionCleanupFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(6, nameof(LateProvisionCleanupFailed)),
            "Late provision cleanup failed.");

    /// <summary>
    /// Records a provision task abandoned by a timed-out or externally cancelled
    /// initialization and schedules its late cleanup. Called under the lifecycle lock.
    /// </summary>
    private void AbandonProvision(Task task)
    {
      _abandonedProvision = task;
      _ = task.ContinueWith(
          static (t, state) =>
          {
            var (self, generation) = ((ResourceBase, int))state!;
            return self.CleanupLateProvisionAsync(t, generation);
          },
          (this, _provisionGeneration),
          CancellationToken.None,
          TaskContinuationOptions.None,
          TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Waits (bounded by the caller's teardown token) for an abandoned provision task so
    /// disposal can tear down whatever it produced while the kernel is still alive, then
    /// bumps the provision generation so the fire-and-forget continuation becomes a no-op.
    /// Called under the lifecycle lock.
    /// </summary>
    private async Task WaitForAbandonedProvisionAsync(CancellationToken cancellationToken)
    {
      var abandoned = _abandonedProvision;
      _abandonedProvision = null;
      _provisionGeneration++;
      if (abandoned is null || abandoned.IsCompleted)
        return;

      // ponytail: bounded grace — a task outliving TeardownTimeout leaks its container
      // until orphan cleanup; the generation fence means it can no longer corrupt state.
      await Task.WhenAny(abandoned, Task.Delay(Timeout.Infinite, cancellationToken))
          .ConfigureAwait(false);
    }

    private async Task CleanupLateProvisionAsync(Task task, int generation)
    {
      try
      {
        await task.ConfigureAwait(false);
      }
      catch
      {
        _ = task.Exception;
        return;
      }

      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        await _lifecycleLock.WaitAsync(cts.Token).ConfigureAwait(false);
        try
        {
          // Generation fence: the resource was disposed or re-initialized since this
          // provision was abandoned. Container may now be a healthy new instance, so
          // removing it here would corrupt live state; DisposeAsync's grace wait owns
          // the normal cleanup path.
          if (generation != _provisionGeneration)
            return;

          await ForceRemoveAsync(cts.Token).ConfigureAwait(false);
          _abandonedProvision = null;
          _provisioned = false;
          IsInitialized = false;
        }
        finally
        {
          _lifecycleLock.Release();
        }
      }
      catch (Exception ex)
      {
        LateProvisionCleanupFailed(Logger, ex);
      }
    }

    private ResourceInitializationException CreateInitializationException(Exception ex)
    {
      var wrapped = new ResourceInitializationException(
          "Resource initialization failed.", Diagnostics, ex);
      CopyData(ex, wrapped, "ContainerLogTail");
      return wrapped;
    }

    private static void CopyData(Exception source, Exception target, string key)
    {
      for (var ex = source; ex != null; ex = ex.InnerException)
      {
        if (ex.Data.Contains(key))
        {
          target.Data[key] = ex.Data[key];
          return;
        }
      }
    }

    private static bool IsExternalCancellation(Exception ex, CancellationToken cancellationToken)
    {
      return cancellationToken.IsCancellationRequested &&
             ex is OperationCanceledException;
    }
  }
}
