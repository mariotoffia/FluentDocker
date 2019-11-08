using System;

namespace Ductus.FluentDocker.Services
{
  public sealed class StateChangeEventArgs : EventArgs
  {
    internal StateChangeEventArgs(IService service, ServiceRunningState state)
    {
      Service = service;
      State = state;
    }

    public IService Service { get; }
    public ServiceRunningState State { get; }
  }
}
