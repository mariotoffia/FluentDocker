using System;
using System.Collections.Generic;
using System.Net;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ContainerBuilder : BaseBuilder<IContainerService>
  {
    private readonly ContainerBuilderConfig _config = new ContainerBuilderConfig();

    internal ContainerBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IContainerService Build()
    {
      var host = FindHostService();
      if (!host.HasValue)
      {
        throw new FluentDockerException(
          $"Cannot build container {_config.Image} since no host service is defined");
      }

      var container = host.Value.Create(_config.Image, _config.CreateParams, _config.StopOnDispose,
        _config.DeleteOnDispose,
        _config.Command, _config.Arguments);

      AddHooks(container);

      return container;
    }

    protected override IBuilder InternalCreate()
    {
      return new ContainerBuilder(this);
    }

    public Builder Builder()
    {
      var builder = FindBuilder();
      if (!builder.HasValue)
      {
        throw new FluentDockerException("Cannot find a parent Builder instance, bug in your code");
      }

      return builder.Value;
    }

    public ContainerBuilder UseImage(string image)
    {
      _config.Image = image;
      return this;
    }

    public ContainerBuilder WithName(string name)
    {
      _config.CreateParams.Name = name;
      return this;
    }

    public ContainerBuilder Command(string command, params string[] arguments)
    {
      _config.Command = command;
      _config.Arguments = arguments;
      return this;
    }

    public ContainerBuilder WithEnvironment(params string[] nameValue)
    {
      _config.CreateParams.Environment = nameValue;
      return this;
    }

    public ContainerBuilder UseEnvironmentFile(params string[] file)
    {
      _config.CreateParams.EnvironmentFiles = _config.CreateParams.EnvironmentFiles.AddToArray(file);
      return this;
    }

    public ContainerBuilder WithParentCGroup(int cgroup)
    {
      _config.CreateParams.ParentCGroup = cgroup.ToString();
      return this;
    }

    public ContainerBuilder UseCapability(params string[] capability)
    {
      _config.CreateParams.CapabilitiesToAdd = _config.CreateParams.CapabilitiesToAdd.AddToArray(capability);
      return this;
    }

    public ContainerBuilder RemoveCapability(params string[] capability)
    {
      _config.CreateParams.CapabilitiesToRemove = _config.CreateParams.CapabilitiesToRemove.AddToArray(capability);
      return this;
    }

    public ContainerBuilder UseVolumeDriver(string driver)
    {
      _config.CreateParams.VolumeDriver = driver;
      return this;
    }

    public ContainerBuilder HostIpMapping(string host, string ip)
    {
      if (null == _config.CreateParams.HostIpMappings)
      {
        _config.CreateParams.HostIpMappings = new List<Tuple<string, IPAddress>>();
      }

      _config.CreateParams.HostIpMappings.Add(new Tuple<string, IPAddress>(host, IPAddress.Parse(ip)));
      return this;
    }

    public ContainerBuilder ExposePort(int hostPort, int containerPort)
    {
      _config.CreateParams.PortMappings = _config.CreateParams.PortMappings.AddToArray($"{hostPort}:{containerPort}");
      return this;
    }

    public ContainerBuilder ExposePort(int containerPort)
    {
      _config.CreateParams.PortMappings = _config.CreateParams.PortMappings.AddToArray($"{containerPort}");
      return this;
    }

    public ContainerBuilder Mount(string fqHostPath, string fqContainerPath, MountType access)
    {
      var hp = OsExtensions.IsWindows()
        ? ((TemplateString) fqHostPath).Rendered.ToMsysPath()
        : ((TemplateString) fqHostPath).Rendered;

      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.AddToArray($"{hp}:{fqContainerPath}:{access.ToDockerMountString()}");
      return this;
    }

    public ContainerBuilder MountFrom(params string[] from)
    {
      _config.CreateParams.VolumesFrom = _config.CreateParams.VolumesFrom.AddToArray(from);
      return this;
    }

    public ContainerBuilder UseWorkDir(string workingDirectory)
    {
      _config.CreateParams.WorkingDirectory = workingDirectory;
      return this;
    }

    public ContainerBuilder Link(params string[] container)
    {
      _config.CreateParams.Links = _config.CreateParams.Links.AddToArray(container);
      return this;
    }

    public ContainerBuilder WithLabel(params string[] label)
    {
      _config.CreateParams.Labels = _config.CreateParams.Labels.AddToArray(label);
      return this;
    }

    public ContainerBuilder UseGroup(params string[] group)
    {
      _config.CreateParams.Groups = _config.CreateParams.Groups.AddToArray(group);
      return this;
    }

    public ContainerBuilder AsUser(string user)
    {
      _config.CreateParams.AsUser = user;
      return this;
    }

    public ContainerBuilder KeepRunning()
    {
      _config.StopOnDispose = false;
      return this;
    }

    public ContainerBuilder KeepContainer()
    {
      _config.DeleteOnDispose = false;
      _config.CreateParams.AutoRemoveContainer = false;
      return this;
    }

    public ContainerBuilder ExportOnDispose(string hostPath, Func<IContainerService, bool> condition = null)
    {
      _config.ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath, false /*no-explode*/,
          condition ?? (svc => true));
      return this;
    }

    public ContainerBuilder ExportExploadedOnDispose(string hostPath, Func<IContainerService, bool> condition = null)
    {
      _config.ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath, true /*explode*/,
          condition ?? (svc => true));
      return this;
    }

    public ContainerBuilder CopyOnStart(string hostPath, string containerPath)
    {
      if (null == _config.CpToOnStart)
      {
        _config.CpToOnStart = new List<Tuple<TemplateString, TemplateString>>();
      }

      _config.CpToOnStart.Add(new Tuple<TemplateString, TemplateString>(hostPath,containerPath));
      return this;
    }

    public ContainerBuilder CopyOnDispose(string containerPath, string hostPath)
    {
      if (null == _config.CpFromOnDispose)
      {
        _config.CpFromOnDispose = new List<Tuple<TemplateString, TemplateString>>();
      }

      _config.CpFromOnDispose.Add(new Tuple<TemplateString, TemplateString>(hostPath,
        containerPath));
      return this;
    }

    public ContainerBuilder WaitForPort(string portAndProto, long millisTimeout = long.MaxValue)
    {
      _config.WaitForPort = new Tuple<string, long>(portAndProto, millisTimeout);
      return this;
    }

    public ContainerBuilder WaitForProcess(string process, long millisTimeout = long.MaxValue)
    {
      _config.WaitForProcess = new Tuple<string, long>(process, millisTimeout);
      return this;
    }

    private void AddHooks(IContainerService container)
    {
      // Copy files just before starting
      if (null != _config.CpToOnStart)
      {
        container.AddHook(ServiceRunningState.Starting,
          service =>
          {
            foreach (var copy in _config.CpToOnStart)
            {
              ((IContainerService)service).CopyTo(copy.Item2, copy.Item1);
            }
          });
      }

      // Wait for port when started
      if (null != _config.WaitForPort)
      {
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            ((IContainerService) service).WaitForPort(_config.WaitForPort.Item1, _config.WaitForPort.Item2);
          });
      }

      // Wait for process when started
      if (null != _config.WaitForProcess)
      {
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            ((IContainerService) service).WaitForProcess(_config.WaitForProcess.Item1, _config.WaitForProcess.Item2);
          });
      }

      // Copy files / folders on dispose
      if (null != _config.CpFromOnDispose && 0 != _config.CpFromOnDispose.Count)
      {
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          foreach (var copy in _config.CpFromOnDispose)
          {
            ((IContainerService) service).CopyFrom(copy.Item2, copy.Item1);
          }
        });
      }

      // Export container on dispose
      if (null != _config.ExportOnDispose)
      {
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          var svc = (IContainerService) service;
          if (_config.ExportOnDispose.Item3(svc))
          {
            svc.Export(_config.ExportOnDispose.Item1,
              _config.ExportOnDispose.Item2);
          }
        });
      }
    }

    private Option<Builder> FindBuilder()
    {
      for (var parent = ((IBuilder) this).Parent; parent.HasValue; parent = parent.Value.Parent)
      {
        var value = parent.Value as Builder;
        if (value != null)
        {
          return new Option<Builder>(value);
        }
      }

      return new Option<Builder>(null);
    }

    private Option<IHostService> FindHostService()
    {
      for (var parent = ((IBuilder) this).Parent; parent.HasValue; parent = parent.Value.Parent)
      {
        var hostService = parent.Value.GetType().GetMethod("Build")?.ReturnType == typeof (IHostService);
        if (hostService)
        {
          return new Option<IHostService>(((IBuilder<IHostService>) parent.Value).Build());
        }
      }

      return new Option<IHostService>(null);
    }
  }
}