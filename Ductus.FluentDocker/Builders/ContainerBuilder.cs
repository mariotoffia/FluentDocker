using System;
using System.Collections.Generic;
using System.Linq;
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

      return host.Value.Create(_config.Image, _config.CreateParams, _config.StopOnDispose, _config.DeleteOnDispose,
        _config.Command, _config.Arguments);
    }

    protected override IBuilder InternalCreate()
    {
      return new ContainerBuilder(this);
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

    public ContainerBuilder ExposePort(int hostPort)
    {
      _config.CreateParams.PortMappings = _config.CreateParams.PortMappings.AddToArray($"{hostPort}");
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

    public ContainerBuilder WaitForPort(string portAndProto, int timeout = int.MaxValue)
    {
      _config.WaitForPort = new Tuple<string, int>(portAndProto, timeout);
      return this;
    }

    private Option<IHostService> FindHostService()
    {
      for (var parent = ((IBuilder) this).Parent; parent.HasValue; parent = parent.Value.Parent)
      {
        if (parent.Value.GetType().GetGenericArguments().Any(x => x == typeof (IHostService)))
        {
          return new Option<IHostService>(((IBuilder<IHostService>) parent.Value).Build());
        }
      }

      return new Option<IHostService>(null);
    }
  }
}