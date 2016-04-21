using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class BuilderCompositeService : ICompositeService
  {
    public BuilderCompositeService(IList<IService> services, string name)
    {
      Services = new ReadOnlyCollection<IService>(services);
      Hosts = new ReadOnlyCollection<IHostService>(services.Where(x => x is IHostService).Cast<IHostService>().ToList());

      Containers =
        new ReadOnlyCollection<IContainerService>(
          services.Where(x => x is IContainerService).Cast<IContainerService>().ToList());

      Name = name;

      foreach (var service in Services)
      {
        service.StateChange += OnStateChange;
      }
    }

    public void Dispose()
    {
      foreach (var service in Services)
      {
        service.Dispose();
      }
    }

    public string Name { get; }

    public ServiceRunningState State
    {
      get
      {
        if (Services.Count == 0)
        {
          return ServiceRunningState.Unknown;
        }

        var state = Services.First().State;
        return Services.All(x => x.State == state) ? state : ServiceRunningState.Unknown;
      }
    }

    void IService.Start()
    {
      foreach (
        var service in
          Services.Where(
            service => service.State != ServiceRunningState.Running))
      {
        service.Start();
      }
    }

    public void Stop()
    {
      foreach (
        var service in
          Services.Where(
            service => service.State != ServiceRunningState.Stopped && service.State != ServiceRunningState.Stopping))
      {
        service.Stop();
      }
    }

    public void Remove(bool force = false)
    {
      foreach (var service in Services)
      {
        service.Remove(force);
      }
    }

    public IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null)
    {
      foreach (var service in Services)
      {
        service.AddHook(state, hook, uniqueName);
      }
      return this;
    }

    public IService RemoveHook(string uniqueName)
    {
      foreach (var service in Services)
      {
        service.RemoveHook(uniqueName);
      }
      return this;
    }

    public event ServiceDelegates.StateChange StateChange;
    public IReadOnlyCollection<IHostService> Hosts { get; }
    public IReadOnlyCollection<IContainerService> Containers { get; }
    public IReadOnlyCollection<IService> Services { get; }

    public ICompositeService Start()
    {
      ((IService) this).Start();
      return this;
    }

    private void OnStateChange(object service, StateChangeEventArgs evt)
    {
      StateChange?.Invoke(service, evt);
    }
  }
}