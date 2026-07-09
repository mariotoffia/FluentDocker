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
    /// Optional token that bounds the total cleanup time.
    /// When cancelled, the current disposal may continue unobserved and remaining
    /// service disposals are not started.
    /// </param>
    public async Task DisposeAllAsync(CancellationToken cancellationToken = default)
    {
      // Reverse creation order: dependents (e.g. containers) before their dependencies
      // (e.g. the networks/volumes they are attached to).
      IServiceAsync[] results;
      lock (_resultsLock)
      {
        results = [.. _results];
        _results.Clear();
      }

      for (var i = results.Length - 1; i >= 0; i--)
      {
        if (cancellationToken.IsCancellationRequested)
        {
          _logger.LogWarning("BuildScope async disposal cancelled; skipping remaining services");
          break;
        }

        var service = results[i];
        try
        {
          var task = service is IAsyncDisposable asyncDisposable
              ? asyncDisposable.DisposeAsync().AsTask()
              : Task.Run(() => service.Dispose(), CancellationToken.None);
          await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
          _logger.LogWarning(ex, "BuildScope async disposal cancelled; skipping remaining services");
          break;
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "BuildScope async disposal failed");
        }
      }
    }

    /// <summary>
    /// Disposes all services in this scope synchronously.
    /// </summary>
    public void DisposeAll()
    {
      // Reverse creation order: dependents before their dependencies.
      IServiceAsync[] results;
      lock (_resultsLock)
      {
        results = [.. _results];
        _results.Clear();
      }

      for (var i = results.Length - 1; i >= 0; i--)
      {
        try
        {
          results[i].Dispose();
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "BuildScope sync disposal failed");
        }
      }
    }
  }
}
