using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
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
      _disposeProvisionGeneration = _provisionGeneration;
      if (abandoned is null || abandoned.IsCompleted)
        return;

      // Bounded grace; late completion is still cleaned unless a re-init owns the resource.
      await Task.WhenAny(abandoned, Task.Delay(Timeout.Infinite, cancellationToken))
          .ConfigureAwait(false);
    }

    private async Task EnsureRuntimeHealthyAsync(CancellationToken cancellationToken)
    {
      if (!await CapabilityChecks.IsHealthyAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false))
      {
        throw new FluentDockerUnavailableException(
            $"Docker driver '{DriverId}' is not reachable. Is Docker running?");
      }
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
          if (generation != _provisionGeneration &&
              _disposeProvisionGeneration != _provisionGeneration)
            return;

          try
          {
            await ForceRemoveAsync(cts.Token).ConfigureAwait(false);
          }
          catch
          {
            // ponytail: mark for orphan reaping ONLY when we could not remove it ourselves
            // (e.g. the kernel is already disposed). On the success path the resource is gone,
            // so marking would leak this name in process-static state and risk wrong-reaping a
            // later same-named (caller-fixed) resource. The rethrow is logged by the outer catch.
            OrphanCleanup.MarkAbandonedLateProvision(ResourceName);
            throw;
          }

          _abandonedProvision = null;
          _disposeProvisionGeneration = 0;
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
