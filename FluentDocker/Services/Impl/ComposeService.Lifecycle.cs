using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  public partial class ComposeService
  {
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
      try
      {
        if (_downOnDispose)
        {
          using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);
          var removeTask = RemoveAsync(force: false, cleanupCts.Token);
          try
          {
            await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "ComposeService DisposeAsync failed");
            ObserveAbandonedCleanup(removeTask);
          }
        }
      }
      finally
      {
        DeleteOwnedTempFiles();
      }
    }

    private void DeleteOwnedTempFiles()
    {
      if (_ownedTempFiles is null)
        return;

      foreach (var path in _ownedTempFiles)
      {
        try
        {
          if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ComposeService failed to delete temp overlay file {Path}", path);
        }
      }
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);
  }
}
