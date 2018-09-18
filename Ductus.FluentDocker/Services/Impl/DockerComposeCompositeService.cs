using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Compose;

namespace Ductus.FluentDocker.Services.Impl
{
  public class DockerComposeCompositeService : ServiceBase, ICompositeService
  {
    private readonly DockerComposeConfig _config;
    public DockerComposeCompositeService(IHostService host, DockerComposeConfig config) : base(config.ComposeFilePath)
    {
      Hosts = new ReadOnlyCollection<IHostService>(new [] {host });
      Containers = new IContainerService[0];
      Images = new IContainerImageService[0];
      Services = new IService[0];
      _config = config;
    }
    public override void Dispose()
    {
    }

    public IReadOnlyCollection<IHostService> Hosts { get; }
    public IReadOnlyCollection<IContainerService> Containers { get; }
    public IReadOnlyCollection<IContainerImageService> Images { get; }
    public IReadOnlyCollection<IService> Services { get; }

    public override void Start()
    {
      if (base.State == ServiceRunningState.Running) return;
      
      State = ServiceRunningState.Starting;
      
      var host = Hosts.First();
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
    }

    ICompositeService ICompositeService.Start()
    {
      Start();
      return this;
    }

    public override void Stop()
    {
      if (!(State == ServiceRunningState.Running || State == ServiceRunningState.Paused)) return;
      
    }

    public override void Remove(bool force = false)
    {
      var host = Hosts.First();
      var result = host.Host.ComposeDown(_config.AlternativeServiceName, _config.ComposeFilePath, _config.ImageRemoval,
        !_config.KeepVolumes, _config.RemoveOrphans, host.Certificates);
    }
  }
}