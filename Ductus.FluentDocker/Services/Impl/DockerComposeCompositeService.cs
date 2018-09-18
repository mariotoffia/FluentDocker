using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Compose;

namespace Ductus.FluentDocker.Services.Impl
{
  public class DockerComposeCompositeService : ServiceBase, ICompositeService
  {
    private readonly DockerComposeConfig _config;
    private IContainerImageService[] _imageCache;
    public DockerComposeCompositeService(IHostService host, DockerComposeConfig config) : base(config.ComposeFilePath)
    {
      Hosts = new ReadOnlyCollection<IHostService>(new [] {host });
      Containers = new IContainerService[0];
      _imageCache = new IContainerImageService[0];
      _config = config;
    }
    public override void Dispose()
    {
      if (_config.StopOnDispose) Stop();
    }

    public IReadOnlyCollection<IHostService> Hosts { get; }
    public IReadOnlyCollection<IContainerService> Containers { get; private set; }

    public IReadOnlyCollection<IService> Services
    {
      get
      {
        var list = new List<IService>();
        
        list.AddRange(Hosts);
        list.AddRange(Containers);
        list.AddRange(Images);
        
        return list.AsReadOnly();
      }
    }


    public IReadOnlyCollection<IContainerImageService> Images
    {
      get
      {
        if (_imageCache.Length > 0) return _imageCache;

        return _imageCache = Containers.Where(x => null != x.Image).Select(x => x.Image).ToArray();
      }
    }

    public override void Start()
    {
      if (base.State == ServiceRunningState.Running) return;

      var host = Hosts.First();
      if (State == ServiceRunningState.Paused)
      {
        var upr = host.Host.ComposeUnPause(_config.AlternativeServiceName, _config.ComposeFilePath, _config.Services,
          host.Certificates);

        if (!upr.Success)
        {
          throw new FluentDockerException($"Could not resume composite service {_config.ComposeFilePath}");
        }

        State = ServiceRunningState.Running;
        return;
      }
      
      State = ServiceRunningState.Starting;
            
      var result = host.Host.ComposeUp(_config.AlternativeServiceName, _config.ComposeFilePath, _config.ForceRecreate,
        _config.NoRecreate, _config.NoBuild, _config.ForceBuild,
        _config.TimeoutSeconds == TimeSpan.Zero ? (TimeSpan?) null : _config.TimeoutSeconds, _config.RemoveOrphans,
        _config.UseColor,
        _config.Services,
        host.Certificates);
      
      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException($"Could not start composite service {_config.ComposeFilePath}");
      }            
      
      State = ServiceRunningState.Running;
      
      var containers = host.Host.ComposePs(_config.AlternativeServiceName, _config.ComposeFilePath, _config.Services,
        host.Certificates);

      if (!containers.Success)
        return;

      Containers = (from cid in containers.Data
        let cinfo = host.Host.InspectContainer(cid, host.Certificates)
        select new DockerContainerService(cinfo.Data.Name, cid, host.Host, cinfo.Data.State.ToServiceState(),
          host.Certificates)).Cast<IContainerService>().ToArray();
    }

    ICompositeService ICompositeService.Start()
    {
      Start();
      return this;
    }

    public override void Stop()
    {
      if (!(State == ServiceRunningState.Running || State == ServiceRunningState.Paused)) return;
      State = ServiceRunningState.Stopping;
      
      var host = Hosts.First();
      
      var result = host.Host.ComposeDown(_config.AlternativeServiceName, _config.ComposeFilePath, _config.ImageRemoval,
        !_config.KeepVolumes, _config.RemoveOrphans, host.Certificates);

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException($"Could not stop composite service {_config.ComposeFilePath}");
      }

      State = ServiceRunningState.Stopped;
    }

    public override void Remove(bool force = false)
    {
      // TODO: ?
      State = ServiceRunningState.Removing;
      State = ServiceRunningState.Removed;
    }
  }
}