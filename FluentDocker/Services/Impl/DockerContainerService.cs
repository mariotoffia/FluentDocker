using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  public sealed class DockerContainerService : IContainerService
  {
    private readonly ServiceHooks _hooks = new ServiceHooks();
    private readonly bool _removeMountOnDispose;
    private readonly bool _removeNamedMountOnDispose;
    private Container _containerConfigCache;
    private IContainerImageService _imgCache;

    private ServiceRunningState _state = ServiceRunningState.Unknown;

    public DockerContainerService(string name, string id, DockerUri docker, ServiceRunningState state,
      ICertificatePaths certificates,
      bool stopOnDispose = true, bool removeOnDispose = true, bool removeMountOnDispose = false,
      bool removeNamedMountOnDispose = false, bool isWindowsContainer = false, string instanceId = null,
      string project = null) : 
      this(name, id, docker, state, certificates, null, stopOnDispose, removeOnDispose, removeMountOnDispose,
      removeNamedMountOnDispose, isWindowsContainer, instanceId, project)
      {
      }

    public DockerContainerService(string name, string id, DockerUri docker, ServiceRunningState state,
      ICertificatePaths certificates,
      Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customEndpointResolver,
      bool stopOnDispose = true, bool removeOnDispose = true, bool removeMountOnDispose = false,
      bool removeNamedMountOnDispose = false, bool isWindowsContainer = false, string instanceId = null,
      string project = null)
    {
      IsWindowsContainer = isWindowsContainer;
      Certificates = certificates;
      _removeNamedMountOnDispose = removeNamedMountOnDispose;
      _removeMountOnDispose = removeMountOnDispose;
      StopOnDispose = stopOnDispose;
      RemoveOnDispose = removeOnDispose;

      Name = name;
      CustomEndpointResolver = customEndpointResolver;
      Id = id;
      Service = project ?? string.Empty;
      InstanceId = instanceId ?? string.Empty;
      DockerHost = docker;
      State = state;
    }

    public string Id { get; }
    public string InstanceId { get; }
    public string Service { get; }

    public DockerUri DockerHost { get; }

    public bool StopOnDispose { get; set; }

    public bool RemoveOnDispose { get; set; }

    public Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> CustomEndpointResolver { get; }

    public bool IsWindowsContainer { get; }

    public IContainerImageService Image
    {
      get
      {
        if (null != _imgCache)
          return _imgCache;

        var images = DockerHost.Images(certificates: Certificates);
        if (!images.Success)
          return null;

        var cfg = GetConfiguration();

        var cfgImageId = cfg.Image;
        var idx = cfgImageId.IndexOf(':');
        if (-1 != idx)
          cfgImageId = cfgImageId.Substring(idx + 1);

        var img = images.Data.FirstOrDefault(x => x.Id == cfgImageId);
        if (null == img)
          return null;

        return _imgCache =
          new DockerImageService(img.Name, img.Id, img.Tags[0], DockerHost, Certificates, IsWindowsContainer);
      }
    }

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
      internal set
      {
        if (_state == value)
          return;

        _state = value;
        this.StateChange?.Invoke(this, new StateChangeEventArgs(this, value));
        _hooks.Execute(this, _state);
      }
    }

    public IContainerService Start()
    {
      ((IService)this).Start();
      return this;
    }

    public void Dispose()
    {
      if (string.IsNullOrEmpty(Id))
        return;

      if (StopOnDispose)
        Stop();

      if (RemoveOnDispose)
        Remove(true, _removeMountOnDispose);
    }

    void IService.Start()
    {
      if (State == ServiceRunningState.Paused)
      {
        var res = DockerHost.UnPause(Certificates, Id);
        if (!res.Success)
          throw new FluentDockerException($"Failed to pause container {Name} log: {res}");
      }
      else
      {
        State = ServiceRunningState.Starting;
        var result = DockerHost.Start(Id, Certificates);
        if (!result.Success)
        {
          Dispose();
          throw new FluentDockerException($"Failed to start container {Name} log: {result}");
        }
      }

      if (GetConfiguration(true).State.Running)
        State = ServiceRunningState.Running;
    }

    void IService.Pause()
    {
      if (State != ServiceRunningState.Running)
        return;
      var result = DockerHost.Pause(Certificates, Id);
      if (!result.Success)
      {
        throw new FluentDockerException($"Failed to pause container {Name} log: {result}");
      }

      State = ServiceRunningState.Paused;
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
      if (State != ServiceRunningState.Stopped && force)
        Stop();

      State = ServiceRunningState.Removing;
      var result = DockerHost.RemoveContainer(Id, force, false, null, Certificates);
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

    public IList<IVolumeService> GetVolumes()
    {
      var config = GetConfiguration();
      var vols = DockerHost.VolumeInspect(Certificates, config.Mounts.Select(x => x.Name).ToArray());
      if (!vols.Success)
        throw new FluentDockerException($"Failed to get attached volumes on docker container {Id}");

      return vols.Data.Select(x => (IVolumeService)new DockerVolumeService(x.Name, DockerHost, Certificates, false))
        .ToList();
    }

    public IList<INetworkService> GetNetworks()
    {
      var config = GetConfiguration();
      var networks = DockerHost.NetworkLs(Certificates);
      if (!networks.Success)
        throw new FluentDockerException($"Failed to get networks that container id = {Id} is attached to");

      var list = new List<INetworkService>();
      foreach (var n in config.NetworkSettings.Networks)
        list.Add(new DockerNetworkService(n.Key, n.Value.NetworkID, DockerHost, Certificates));

      return list;
    }

    private void Remove(bool force, bool removeVolume)
    {
      if (State != ServiceRunningState.Stopped && force)
        Stop();

      State = ServiceRunningState.Removing;
      var result = DockerHost.RemoveContainer(Id, force, removeVolume, null, Certificates);

      if (_removeNamedMountOnDispose)
      {
        var config = GetConfiguration();
        if (null != config)
        {
          var namedMounts = config.Mounts.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name).ToArray();
          DockerHost.VolumeRm(Certificates, true /*force*/, namedMounts);
        }
      }

      if (result.Success)
        State = ServiceRunningState.Removed;
    }
  }
}
