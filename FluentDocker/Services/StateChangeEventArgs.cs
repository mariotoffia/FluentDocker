using System;

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Services
{
  /// <summary>
  /// Event arguments for service state change notifications.
  /// </summary>
  public sealed class StateChangeEventArgs : EventArgs
  {
    internal StateChangeEventArgs(IService service, ServiceRunningState state)
    {
      Service = service;
      State = state;
    }

    /// <summary>The service whose state changed.</summary>
    public IService Service { get; }

    /// <summary>The new running state.</summary>
    public ServiceRunningState State { get; }
  }
}
