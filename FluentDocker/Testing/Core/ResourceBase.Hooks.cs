using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Testing.Core
{
  public abstract partial class ResourceBase
  {
    private async Task RunHooksAsync(
        List<Func<ITestResource, Task>> hooks,
        CancellationToken cancellationToken)
    {
      foreach (var hook in hooks)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var hookTask = hook(this);
        var completed = await Task.WhenAny(
            hookTask,
            Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);

        if (completed != hookTask)
          cancellationToken.ThrowIfCancellationRequested();

        await hookTask.ConfigureAwait(false);
      }
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
  }
}
