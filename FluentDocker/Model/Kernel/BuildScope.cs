using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Services;

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Model.Kernel
{
  /// <summary>
  /// Represents a build scope (kernel + driver).
  /// All operations within a scope use the same kernel and driver.
  /// </summary>
  public class BuildScope
  {
    private readonly List<IService> _results = new List<IService>();

    /// <summary>
    /// Creates a new build scope.
    /// </summary>
    /// <param name="kernel">The kernel instance</param>
    /// <param name="driverId">The driver identifier</param>
    public BuildScope(global::FluentDocker.Kernel.FluentDockerKernel kernel, string driverId)
    {
      Kernel = kernel;
      DriverId = driverId;
    }

    /// <summary>
    /// Gets the kernel for this scope.
    /// </summary>
    public global::FluentDocker.Kernel.FluentDockerKernel Kernel { get; }

    /// <summary>
    /// Gets the driver ID for this scope.
    /// </summary>
    public string DriverId { get; }

    /// <summary>
    /// Gets the results (services) for this scope.
    /// </summary>
    public IReadOnlyList<IService> Results => _results;

    /// <summary>
    /// Adds a result to this scope.
    /// </summary>
    /// <param name="service">Service to add</param>
    public void AddResult(IService service)
    {
      if (service != null)
      {
        _results.Add(service);
      }
    }

    /// <summary>
    /// Disposes all services in this scope asynchronously.
    /// </summary>
    /// <param name="cancellationToken">
    /// Optional token that bounds the total cleanup time.
    /// When cancelled, remaining service disposals are abandoned.
    /// </param>
    public async Task DisposeAllAsync(CancellationToken cancellationToken = default)
    {
      foreach (var service in _results)
      {
        try
        {
          var task = service is IAsyncDisposable asyncDisposable
              ? asyncDisposable.DisposeAsync().AsTask()
              : Task.Run(() => service.Dispose(), CancellationToken.None);
          await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          Logger.Log($"BuildScope async disposal failed: {ex.Message}");
        }
      }
      _results.Clear();
    }

    /// <summary>
    /// Disposes all services in this scope synchronously.
    /// </summary>
    public void DisposeAll()
    {
      foreach (var service in _results)
      {
        try
        {
          service.Dispose();
        }
        catch (Exception ex)
        {
          Logger.Log($"BuildScope sync disposal failed: {ex.Message}");
        }
      }
      _results.Clear();
    }
  }
}
