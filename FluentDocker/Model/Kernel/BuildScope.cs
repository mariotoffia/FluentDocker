using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Model.Kernel
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
    private readonly ILogger<BuildScope> _logger = kernel.LoggerFactory.CreateLogger<BuildScope>();

    /// <summary>
    /// Gets the kernel for this scope.
    /// </summary>
    public global::FluentDocker.Kernel.FluentDockerKernel Kernel { get; } = kernel;

    /// <summary>
    /// Gets the driver ID for this scope.
    /// </summary>
    public string DriverId { get; } = driverId;

    /// <summary>
    /// Gets the results (services) for this scope.
    /// </summary>
    public IReadOnlyList<IServiceAsync> Results => _results;

    /// <summary>
    /// Adds a result to this scope.
    /// </summary>
    /// <param name="service">Service to add</param>
    public void AddResult(IServiceAsync service)
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
          _logger.LogWarning(ex, "BuildScope async disposal failed");
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
          _logger.LogWarning(ex, "BuildScope sync disposal failed");
        }
      }
      _results.Clear();
    }
  }
}
