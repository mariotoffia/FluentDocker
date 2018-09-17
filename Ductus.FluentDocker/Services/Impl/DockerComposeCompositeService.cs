using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Services.Impl
{
  public class DockerComposeCompositeService : ICompositeService
  {
    public DockerComposeCompositeService()
    {
      
    }
    public void Dispose()
    {
      throw new NotImplementedException();
    }

    public string Name { get; }
    public ServiceRunningState State { get; }
    public IReadOnlyCollection<IHostService> Hosts { get; }
    public IReadOnlyCollection<IContainerService> Containers { get; }
    public IReadOnlyCollection<IContainerImageService> Images { get; }
    public IReadOnlyCollection<IService> Services { get; }

    void IService.Start()
    {
      throw new NotImplementedException();
    }

    ICompositeService ICompositeService.Start()
    {
      throw new NotImplementedException();
    }

    public void Stop()
    {
      throw new NotImplementedException();
    }

    public void Remove(bool force = false)
    {
      throw new NotImplementedException();
    }

    public IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null)
    {
      throw new NotImplementedException();
    }

    public IService RemoveHook(string uniqueName)
    {
      throw new NotImplementedException();
    }

    public event ServiceDelegates.StateChange StateChange;
  }
}