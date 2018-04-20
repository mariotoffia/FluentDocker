using System;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerContainerService : IContainerService
  {
    private readonly ServiceHooks _hooks = new ServiceHooks();
    private readonly bool _isWindowsContainer;
    private readonly bool _removeOnDispose;
    private readonly bool _stopOnDispose;
    private readonly bool _removeMountOnDispose;
    private Container _containerConfigCache;
    private ServiceRunningState _state = ServiceRunningState.Unknown;

    public DockerContainerService(string name, string id, DockerUri docker, ServiceRunningState state,
      ICertificatePaths certificates,
      bool stopOnDispose = true, bool removeOnDispose = true, bool removeMountOnDispose = false, bool isWindowsContainer = false)
    {
      _isWindowsContainer = isWindowsContainer;
      Certificates = certificates;
      _removeMountOnDispose = removeMountOnDispose;
      _stopOnDispose = stopOnDispose;
      _removeOnDispose = removeOnDispose;

      Name = name;
      Id = id;
      DockerHost = docker;
      State = state;
    }

    public string Id { get; }
    public DockerUri DockerHost { get; }

    public Container GetConfiguration(bool fresh = false)
    {
      if (!fresh && null != _containerConfigCache)
        return _containerConfigCache;

      _containerConfigCache = DockerHost.InspectContainer(Id, Certificates).Data;
      return _containerConfigCache;
    }

    public ICertificatePaths Certificates { get; }

    public string Name { get; }

    public ServiceRunningState State
    {
      get => _state;
      set
      {
        if (_state == value)
          return;

        _state = value;
        StateChange?.Invoke(this, new StateChangeEventArgs(this, value));
        _hooks.Execute(this, _state);
      }
    }

    public IContainerService Start()
    {
      ((IService) this).Start();
      return this;
    }

    public void Dispose()
    {
      if (string.IsNullOrEmpty(Id))
        return;

      if (_stopOnDispose)
        Stop();

      if (_removeOnDispose)
        Remove(true, _removeMountOnDispose);
    }

    void IService.Start()
    {
      State = ServiceRunningState.Starting;
      DockerHost.Start(Id, Certificates);
      if (GetConfiguration().State.Running)
        State = ServiceRunningState.Running;
    }

    public void Stop()
    {
      State = ServiceRunningState.Stopping;
      var res = DockerHost.Stop(Id, null, Certificates);
      if (res.Success)
        State = ServiceRunningState.Stopped;
    }

    public void Remove(bool force = false)
    {
      if (State != ServiceRunningState.Stopped)
        Stop();

      State = ServiceRunningState.Removing;
      var result = DockerHost.RemoveContainer(Id, force, false, null, Certificates);
      if (result.Success)
        State = ServiceRunningState.Removed;
    }
    private void Remove(bool force, bool removeVolume)
    {
      if (State != ServiceRunningState.Stopped)
        Stop();

      State = ServiceRunningState.Removing;
      var result = DockerHost.RemoveContainer(Id, force, removeVolume, null, Certificates);
      if (result.Success)
        State = ServiceRunningState.Removed;
    }

    public IService AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName = null)
    {
      _hooks.AddHook(uniqueName ?? Guid.NewGuid().ToString(), state, hook);
      return this;
    }

    public IService RemoveHook(string uniqueName)
    {
      _hooks.RemoveHook(uniqueName);
      return this;
    }

    public event ServiceDelegates.StateChange StateChange;
  }
}