using System;

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
  /// Event arguments for service state change notifications.
  /// </summary>
  public sealed class StateChangeEventArgs : EventArgs
  {
    internal StateChangeEventArgs(IServiceAsync service, ServiceRunningState state)
    {
      Service = service;
      State = state;
    }

    /// <summary>The service whose state changed.</summary>
    public IServiceAsync Service { get; }

    /// <summary>The new running state.</summary>
    public ServiceRunningState State { get; }
  }
}
