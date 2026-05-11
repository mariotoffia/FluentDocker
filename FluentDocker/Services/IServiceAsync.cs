using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;

namespace FluentDocker.Services
{
  /// <summary>
  /// Async service interface with kernel/driver architecture.
  /// This is the root service interface for all FluentDocker services.
  /// </summary>
  public interface IServiceAsync : IDisposable, IAsyncDisposable
  {
    /// <summary>Name or identifier of the service.</summary>
    string Name { get; }

    /// <summary>Current running state of the service.</summary>
    ServiceRunningState State { get; }

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
    /// Adds a state change hook.
    /// </summary>
    IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null);

    /// <summary>
    /// Removes a hook by name.
    /// </summary>
    IServiceAsync RemoveHook(string uniqueName);

    /// <summary>Raised when the service transitions to a new running state.</summary>
#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710
  }
}
