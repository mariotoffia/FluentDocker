using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;

namespace FluentDocker.Services
{
  /// <summary>
  /// Async service interface with kernel/driver architecture.
  /// This is the preferred service interface — use this instead of <see cref="IService"/>.
  /// </summary>
#pragma warning disable CS0618 // IServiceAsync intentionally extends IService for backwards compat
  public interface IServiceAsync : IService, IAsyncDisposable
#pragma warning restore CS0618
  {
    /// <summary>
    /// The kernel instance managing this service.
    /// </summary>
    FluentDockerKernel Kernel { get; }

    /// <summary>
    /// The driver ID used by this service.
    /// </summary>
    string DriverId { get; }

    /// <summary>
    /// Starts the service asynchronously.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the service asynchronously (if supported).
    /// </summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service asynchronously.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the service asynchronously.
    /// </summary>
    Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a state change hook (async version).
    /// </summary>
    IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null);

    /// <summary>
    /// Removes a hook by name (async version).
    /// </summary>
    new IServiceAsync RemoveHook(string uniqueName);
  }
}

