#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Represents a build scope (kernel + driver).
  /// All operations within a scope use the same kernel and driver.
  /// </summary>
  /// <remarks>
  /// Creates a new build scope.
  /// </remarks>
  /// <param name="kernel">The kernel instance</param>
  /// <param name="driverId">The driver identifier</param>
  public class BuildScope(global::FluentDocker.Kernel.FluentDockerKernel kernel, string driverId)
  {
    private readonly List<IServiceAsync> _results = [];
    private readonly object _resultsLock = new object();
    private readonly ILogger<BuildScope> _logger = kernel.LoggerFactory.CreateLogger<BuildScope>();

    /// <summary>
    /// Gets the kernel for this scope.
    /// </summary>
    // ponytail: ISysCtl still lives in FluentDocker.Kernel; moving it in v3 would ripple through the public API.
    public ISysCtl Kernel { get; } = kernel;

    /// <summary>
    /// Gets the driver ID for this scope.
    /// </summary>
    public string DriverId { get; } = driverId;

    /// <summary>
    /// Gets a snapshot of the results (services) for this scope.
    /// </summary>
    /// <remarks>
    /// <see cref="AddResult"/> is synchronized so future parallel build operations
    /// cannot corrupt the backing list.
    /// </remarks>
    public IReadOnlyList<IServiceAsync> Results
    {
      get
      {
        lock (_resultsLock)
        {
          return [.. _results];
        }
      }
    }

    /// <summary>
    /// Adds a result to this scope.
    /// </summary>
    /// <param name="service">Service to add</param>
    public void AddResult(IServiceAsync service)
    {
      if (service != null)
      {
        lock (_resultsLock)
        {
          _results.Add(service);
        }
      }
    }

    /// <summary>
    /// Disposes all services in this scope asynchronously.
    /// </summary>
    /// <param name="cancellationToken">
    /// Optional token that bounds cleanup. Services are removed from this scope only
    /// after disposal completes; cancelled, timed-out, or failed disposals remain
    /// observable in <see cref="Results"/> so callers can retry cleanup.
    /// </param>
    public Task DisposeAllAsync(CancellationToken cancellationToken = default)
    {
      return DisposeAllAsyncCore(null, cancellationToken);
    }

    internal Task DisposeAllAsync(TimeSpan perServiceTimeout, CancellationToken cancellationToken = default)
    {
      return DisposeAllAsyncCore(perServiceTimeout, cancellationToken);
    }

    private async Task DisposeAllAsyncCore(
        TimeSpan? perServiceTimeout, CancellationToken cancellationToken)
    {
      // Reverse creation order: dependents (e.g. containers) before their dependencies
      // (e.g. the networks/volumes they are attached to).
      var results = SnapshotResults();

      for (var i = results.Length - 1; i >= 0; i--)
      {
        if (cancellationToken.IsCancellationRequested)
        {
          _logger.LogWarning("BuildScope async disposal cancelled; retaining remaining services");
          break;
        }

        var service = results[i];
        try
        {
          await DisposeServiceAsync(service, perServiceTimeout, cancellationToken)
              .ConfigureAwait(false);
          RemoveResult(service);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
          _logger.LogWarning(ex, "BuildScope async disposal cancelled; retaining remaining services");
          break;
        }
        catch (TimeoutException ex)
        {
          _logger.LogWarning(ex, "BuildScope async disposal timed out; service retained for retry");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "BuildScope async disposal failed; service retained for retry");
        }
      }
    }

    /// <summary>
    /// Disposes all services in this scope synchronously.
    /// </summary>
    public void DisposeAll()
    {
      // Reverse creation order: dependents before their dependencies.
      var results = SnapshotResults();

      for (var i = results.Length - 1; i >= 0; i--)
      {
        try
        {
          results[i].Dispose();
          RemoveResult(results[i]);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "BuildScope sync disposal failed; service retained for retry");
        }
      }
    }

    private IServiceAsync[] SnapshotResults()
    {
      lock (_resultsLock)
      {
        return [.. _results];
      }
    }

    private void RemoveResult(IServiceAsync service)
    {
      lock (_resultsLock)
      {
        for (var i = _results.Count - 1; i >= 0; i--)
        {
          if (ReferenceEquals(_results[i], service))
          {
            _results.RemoveAt(i);
            return;
          }
        }
      }
    }

    private static async Task DisposeServiceAsync(
        IServiceAsync service, TimeSpan? perServiceTimeout, CancellationToken cancellationToken)
    {
      var task = service is IAsyncDisposable asyncDisposable
          ? asyncDisposable.DisposeAsync().AsTask()
          : Task.Run(() => service.Dispose(), CancellationToken.None);
      if (perServiceTimeout == null)
      {
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return;
      }

      using var timeoutCts = new CancellationTokenSource(perServiceTimeout.Value);
      if (cancellationToken.CanBeCanceled)
      {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);
        await WaitWithTimeoutAsync(task, timeoutCts, linkedCts.Token, cancellationToken)
            .ConfigureAwait(false);
        return;
      }

      await WaitWithTimeoutAsync(task, timeoutCts, timeoutCts.Token, cancellationToken)
          .ConfigureAwait(false);
    }

    private static async Task WaitWithTimeoutAsync(
        Task task,
        CancellationTokenSource timeoutCts,
        CancellationToken waitToken,
        CancellationToken cancellationToken)
    {
      try
      {
        await task.WaitAsync(waitToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
      {
        throw new TimeoutException("Timed out disposing build service.");
      }
    }
  }
}
