using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Services.Impl
{
  [Experimental(TargetVersion = "3.0.0")]
  public class DockerComposeCompositeService : ServiceBase, ICompositeService
  {
    private readonly DockerComposeConfig _config;
    private IContainerImageService[] _imageCache;

    public DockerComposeCompositeService(IHostService host, DockerComposeConfig config) : base(config.ComposeFilePath
      .First())
    {
      Hosts = new ReadOnlyCollection<IHostService>(new[] { host });
      Containers = new IContainerService[0];
      _imageCache = new IContainerImageService[0];
      _config = config;
    }

    public override void Dispose()
    {
      try
      {
        if (_config.StopOnDispose)
        {
          Stop();
        }

        if (_config.KeepContainers)
          return;

        State = ServiceRunningState.Removing;
        var host = Hosts.First();

        var result = host.Host.ComposeDown(_config.AlternativeServiceName, _config.ImageRemoval,
          !_config.KeepVolumes, _config.RemoveOrphans, _config.EnvironmentNameValue, host.Certificates,
          _config.ComposeFilePath.ToArray());

        if (!result.Success)
        {
          State = ServiceRunningState.Unknown;
          throw new FluentDockerException($"Could not dispose composite service from file(s) {string.Join(", ", _config.ComposeFilePath)}");
        }

        State = ServiceRunningState.Removed;
      }
      finally
      {
        Hosts = new ReadOnlyCollection<IHostService>(new List<IHostService>());
        Containers = new IContainerService[0];
        _imageCache = new IContainerImageService[0];
      }
    }

    public override ServiceRunningState State
    {
      get => base.State;
      protected set
      {
        if (State == value)
        {
          return;
        }

        base.State = value;
        if (null == Containers)
          return;
        foreach (var container in Containers.Cast<DockerContainerService>())
        {
          container.State = value;
        }
      }
    }

    public IReadOnlyCollection<IHostService> Hosts { get; private set; }
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
        if (_imageCache.Length > 0)
          return _imageCache;

        return _imageCache = Containers.Where(x => null != x.Image).Select(x => x.Image).ToArray();
      }
    }

    public override void Start()
    {
      if (State == ServiceRunningState.Running)
        return;

      var host = Hosts.First();
      if (State == ServiceRunningState.Paused)
      {
        var upr = host.Host.ComposeUnPause(_config.AlternativeServiceName, _config.Services, _config.EnvironmentNameValue,
          host.Certificates, _config.ComposeFilePath.ToArray());

        if (!upr.Success)
          throw new FluentDockerException($"Could not resume composite service from file(s) {string.Join(", ", _config.ComposeFilePath)}");

        State = ServiceRunningState.Running;
        return;
      }

      State = ServiceRunningState.Starting;

      var result = host.Host.ComposeUp(_config.AlternativeServiceName, _config.ForceRecreate,
        _config.NoRecreate, _config.NoBuild, _config.ForceBuild,
        _config.TimeoutSeconds == TimeSpan.Zero ? (TimeSpan?)null : _config.TimeoutSeconds, _config.RemoveOrphans,
        _config.UseColor,
        true/*noStart*/,
        _config.Services,
        _config.EnvironmentNameValue,
        host.Certificates, _config.ComposeFilePath.ToArray());

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException(
          $"Could not create composite service with file(s) {string.Join(", ", _config.ComposeFilePath)} - result: {result}");
      }

      State = ServiceRunningState.Starting;

      result = host.Host.ComposeUp(_config.AlternativeServiceName, 
        false/*forceRecreate*/,false/*noRecreate*/,false/*dontBuild*/, false/*buildBeforeCreate*/,
        _config.TimeoutSeconds == TimeSpan.Zero ? (TimeSpan?)null : _config.TimeoutSeconds, _config.RemoveOrphans,
        _config.UseColor,
        true/*noStart*/,
        _config.Services,
        _config.EnvironmentNameValue,
        host.Certificates, _config.ComposeFilePath.ToArray());

      if (!result.Success)
      {
        throw new FluentDockerException(
          $"Could not start composite service with file(s) {string.Join(", ", _config.ComposeFilePath)} - result: {result}");
      }

      var containers = host.Host.ComposePs(_config.AlternativeServiceName, _config.Services, _config.EnvironmentNameValue,
        host.Certificates, _config.ComposeFilePath.ToArray());

      if (!containers.Success)
        return;

      var list = new List<IContainerService>();
      foreach (var cid in containers.Data)
      {
        var info = host.Host.InspectContainer(cid, host.Certificates);
        var name = ExtractNames(info.Data, out var project, out var instanceId);

        list.Add(new DockerContainerService(name, cid, host.Host, info.Data.State.ToServiceState(),
          host.Certificates, instanceId: instanceId, project: project));
      }

      Containers = list;
      State = ServiceRunningState.Running;
    }

    public override void Pause()
    {
      if (State != ServiceRunningState.Running)
        return;

      var host = Hosts.First();
      var pause = host.Host.ComposePause(_config.AlternativeServiceName, _config.Services, _config.EnvironmentNameValue,
        host.Certificates, _config.ComposeFilePath.ToArray());

      if (!pause.Success)
        throw new FluentDockerException($"Could not pause composite service from file(s) {string.Join(", ", _config.ComposeFilePath)}");

      State = ServiceRunningState.Paused;
    }

    ICompositeService ICompositeService.Start()
    {
      Start();
      return this;
    }

    public override void Stop()
    {
      if (!(State == ServiceRunningState.Running || State == ServiceRunningState.Starting ||
            State == ServiceRunningState.Paused))
        return;

      State = ServiceRunningState.Stopping;

      var host = Hosts.First();

      var result = host.Host.ComposeStop(_config.AlternativeServiceName, TimeSpan.FromSeconds(30),
        _config.Services, _config.EnvironmentNameValue, host.Certificates, _config.ComposeFilePath.ToArray());

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException($"Could not stop composite service from file(s) {string.Join(", ", _config.ComposeFilePath)}");
      }

      State = ServiceRunningState.Stopped;
    }

    public override void Remove(bool force = false)
    {
      State = ServiceRunningState.Removing;
      var host = Hosts.First();

      var result = host.Host.ComposeRm(_config.AlternativeServiceName, force,
        !_config.KeepVolumes, _config.Services, _config.EnvironmentNameValue, host.Certificates, _config.ComposeFilePath.ToArray());

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException($"Could not remove composite service from file(s) {string.Join(", ", _config.ComposeFilePath)}");
      }

      State = ServiceRunningState.Removed;
    }

    private static string ExtractNames(Container container, out string project, out string instanceId)
    {
      var name = container.Name;
      if (name.StartsWith("/"))
        name = name.Substring(1);

      var components = name.Split('_');
      if (components.Length >= 3)
      {
        project = components[0];
        instanceId = components[2];
        return components[1];
      }

      // use labels instead of name of the container
      project = container.Config.Labels["com.docker.compose.project"];
      instanceId = container.Config.Labels["com.docker.compose.container-number"];

      return container.Name;
    }
  }
}
