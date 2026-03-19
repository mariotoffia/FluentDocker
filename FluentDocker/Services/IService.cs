using System;
using FluentDocker.Common;

namespace FluentDocker.Services
{
  /// <summary>
  /// Container for service-related delegate types.
  /// </summary>
  public sealed class ServiceDelegates
  {
    /// <summary>Delegate for service state change notifications.</summary>
    public delegate void StateChange(object sender, StateChangeEventArgs evt);
  }

  /// <summary>
  /// Synchronous service interface. Use <see cref="IServiceAsync"/> for async operations.
  /// </summary>
  [Obsolete("Use IServiceAsync instead. Sync methods wrap async with .GetAwaiter().GetResult() " +
            "which can deadlock on single-threaded synchronization contexts. " +
            "This interface will be removed in v4.")]
  public interface IService : IDisposable
  {
    /// <summary>Name or identifier of the service.</summary>
    string Name { get; }

    /// <summary>Current running state of the service.</summary>
    ServiceRunningState State { get; }
    /// <summary>
    /// Starts a service either from scratch or un-pause the service if earlier paused by <see cref="Pause"/>.
    /// </summary>
    void Start();
    /// <summary>
    /// Pauses the service (if it supports such) and may be resumed by <see cref="Start"/>.
    /// </summary>
    /// <exception cref="FluentDockerNotSupportedException">If any the service do not support this operation.</exception>
    /// <remarks>
    /// Some services may implement this functionality and some it makes no sense or it is impossible
    /// to pause the service. When the service is paused it will be reflected as <see cref="ServiceRunningState.Paused"/>
    /// in the <see cref="State"/> property.
    /// </remarks>
    void Pause();
    /// <summary>Stops the service.</summary>
#pragma warning disable CA1716 // 'Stop' conflicts with reserved keyword — intentional API design
    void Stop();
#pragma warning restore CA1716

    /// <summary>Removes the service and its associated resources.</summary>
    void Remove(bool force = false);

    /// <summary>Registers a hook to be called when the service reaches the specified state.</summary>
    IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null);

    /// <summary>Removes a previously registered hook by its unique name.</summary>
    IService RemoveHook(string uniqueName);

    /// <summary>Raised when the service transitions to a new running state.</summary>
#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710
  }
}
