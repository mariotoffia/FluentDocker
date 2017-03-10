using System;

namespace Ductus.FluentDocker.Services
{
  public sealed class ServiceDelegates
  {
    public delegate void StateChange(object sender, StateChangeEventArgs evt);
  }

  public interface IService : IDisposable
  {
    string Name { get; }
    ServiceRunningState State { get; }
    void Start();
    void Stop();
    void Remove(bool force = false);
    IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null);
    IService RemoveHook(string uniqueName);

    event ServiceDelegates.StateChange StateChange;
  }
}