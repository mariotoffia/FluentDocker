using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using static Ductus.FluentDocker.Commands.Compose;

namespace Ductus.FluentDocker.Services.Impl
{
  [Experimental(TargetVersion = "3.0.0")]
  public class DockerComposeCompositeService : ServiceBase, ICompositeService
  {
    private IContainerImageService[] _imageCache;

    protected DockerComposeConfig Config { get; }

    public DockerComposeCompositeService(IHostService host, DockerComposeConfig config) : base(config.ComposeFilePath
      .First())
    {
      Hosts = new ReadOnlyCollection<IHostService>(new[] { host });
      Containers = new IContainerService[0];
      _imageCache = new IContainerImageService[0];
      Config = config;
      
      // Auto-detect Docker Compose version if not explicitly set
      if (Config.ComposeVersion == ComposeVersion.Unknown)
      {
        throw new FluentDockerException(
          "Compose version must be set. Use AutoDetectComposeVersion() or AssumeComposeVersion() in the builder.");
      }
    }
    

    public override void Dispose()
    {
      try
      {
        if (Config.StopOnDispose)
        {
          Stop();
        }

        if (Config.KeepContainers)
          return;

        State = ServiceRunningState.Removing;
        var host = Hosts.First();

        var result = host.Host.ComposeDown(Config.AlternativeServiceName, Config.ImageRemoval,
          !Config.KeepVolumes, Config.RemoveOrphans, Config.EnvironmentNameValue, host.Certificates,
          Config.ComposeFilePath.ToArray());

        if (!result.Success)
        {
          State = ServiceRunningState.Unknown;
          throw new FluentDockerException($"Could not dispose composite service from file(s) {string.Join(", ", Config.ComposeFilePath)}");
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

    public IReadOnlyCollection<IHostService> Hosts { get; protected set; }
    public IReadOnlyCollection<IContainerService> Containers { get; protected set; }

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
        var upr = host.Host.ComposeUnPause(Config.AlternativeServiceName, Config.Services, Config.EnvironmentNameValue,
          host.Certificates, Config.ComposeFilePath.ToArray());

        if (!upr.Success)
          throw new FluentDockerException($"Could not resume composite service from file(s) {string.Join(", ", Config.ComposeFilePath)}");

        State = ServiceRunningState.Running;
        return;
      }

      State = ServiceRunningState.Starting;

      if (Config.AlwaysPull)
      {
        var resultPull = host.Host.ComposePull(
          new ComposePullCommandArgs
          {

            AltProjectName = Config.AlternativeServiceName,
            Services = Config.Services,
            Env = Config.EnvironmentNameValue,
            Certificates = host.Certificates,
            ComposeFiles = Config.ComposeFilePath,
            DownloadAllTagged = false,
            SkipImageVerification = false,
          });

        if (!resultPull.Success)
        {
          State = ServiceRunningState.Unknown;
          throw new FluentDockerException(
            $"Could not pull composite service with file(s) {string.Join(", ", Config.ComposeFilePath)} - result: {resultPull}");
        }
      }


      var result = host.Host.ComposeUpCommand(
        new ComposeUpCommandArgs
        {
          AltProjectName = Config.AlternativeServiceName,
          ForceRecreate = Config.ForceRecreate,
          NoRecreate = Config.NoRecreate,
          DontBuild = Config.NoBuild,
          BuildBeforeCreate = Config.ForceBuild,
          Timeout = Config.TimeoutSeconds == TimeSpan.Zero ? (TimeSpan?)null : Config.TimeoutSeconds,
          RemoveOrphans = Config.RemoveOrphans,
          UseColor = Config.UseColor,
          NoStart = true,
          Services = Config.Services,
          Env = Config.EnvironmentNameValue,
          Certificates = host.Certificates,
          ComposeFiles = Config.ComposeFilePath.ToArray(),
          ProjectDirectory = Config.ProjectDirectory
        });

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException(
          $"Could not create composite service with file(s) {string.Join(", ", Config.ComposeFilePath)} - result: {result}");
      }

      State = ServiceRunningState.Starting;

      result = host.Host.ComposeUpCommand(
        new ComposeUpCommandArgs
        {
          AltProjectName = Config.AlternativeServiceName,
          ForceRecreate = false,
          NoRecreate = false,
          DontBuild = false,
          BuildBeforeCreate = false,
          Timeout = Config.TimeoutSeconds == TimeSpan.Zero ? (TimeSpan?)null : Config.TimeoutSeconds,
          RemoveOrphans = Config.RemoveOrphans,
          UseColor = Config.UseColor,
          NoStart = false,
          Wait = Config.Wait,
          WaitTimeoutSeconds = Config.WaitTimeoutSeconds,
          Services = Config.Services,
          Env = Config.EnvironmentNameValue,
          Certificates = host.Certificates,
          ComposeFiles = Config.ComposeFilePath.ToArray(),
          ProjectDirectory = Config.ProjectDirectory
        });

      if (!result.Success)
      {
        throw new FluentDockerException(
          $"Could not start composite service with file(s) {string.Join(", ", Config.ComposeFilePath)} - result: {result}");
      }

      var containers = host.Host.ComposePs(Config.AlternativeServiceName, Config.Services, Config.EnvironmentNameValue,
        host.Certificates, Config.ComposeFilePath.ToArray());

      if (!containers.Success)
        return;

      var list = new List<IContainerService>();
      foreach (var cid in containers.Data)
      {
        var info = host.Host.InspectContainer(cid, host.Certificates);
        var name = ExtractNames(info.Data, out var project, out var instanceId);

        list.Add(new DockerContainerService(name, cid, host.Host, info.Data.State.ToServiceState(),
          host.Certificates, null/*noCustomResolver*/, instanceId: instanceId, project: project));
      }

      Containers = list;
      State = ServiceRunningState.Running;
    }

    public override void Pause()
    {
      if (State != ServiceRunningState.Running)
        return;

      var host = Hosts.First();
      var pause = host.Host.ComposePause(Config.AlternativeServiceName, Config.Services, Config.EnvironmentNameValue,
        host.Certificates, Config.ComposeFilePath.ToArray());

      if (!pause.Success)
        throw new FluentDockerException($"Could not pause composite service from file(s) {string.Join(", ", Config.ComposeFilePath)}");

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

      var result = host.Host.ComposeStop(Config.AlternativeServiceName, TimeSpan.FromSeconds(30),
        Config.Services, Config.EnvironmentNameValue, host.Certificates, Config.ComposeFilePath.ToArray());

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException($"Could not stop composite service from file(s) {string.Join(", ", Config.ComposeFilePath)}");
      }

      State = ServiceRunningState.Stopped;
    }

    public override void Remove(bool force = false)
    {
      State = ServiceRunningState.Removing;
      var host = Hosts.First();

      var result = host.Host.ComposeRm(Config.AlternativeServiceName, force,
        !Config.KeepVolumes, Config.Services, Config.EnvironmentNameValue, host.Certificates, Config.ComposeFilePath.ToArray());

      if (!result.Success)
      {
        State = ServiceRunningState.Unknown;
        throw new FluentDockerException($"Could not remove composite service from file(s) {string.Join(", ", Config.ComposeFilePath)}");
      }

      State = ServiceRunningState.Removed;
    }

    protected virtual string ExtractNames(Container container, out string project, out string instanceId)
    {
      var componentSeparator = Config.ComposeVersion switch
      {
        ComposeVersion.Unknown or ComposeVersion.V1 => '_',
        ComposeVersion.V2 => '-',
        _ => throw new InvalidOperationException(
                    $"Unrecognised compose version specified for {nameof(DockerComposeConfig)}.{nameof(DockerComposeConfig.ComposeVersion)}"),
      };

      var name = container.Name;
      if (name.StartsWith("/"))
        name = name.Substring(1);

      var components = name.Split(componentSeparator);
      
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
