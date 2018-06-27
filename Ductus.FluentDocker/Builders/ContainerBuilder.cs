using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;

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
        throw new FluentDockerException(
          $"Cannot build container {_config.Image} since no host service is defined");

      if (_config.VerifyExistence && !string.IsNullOrEmpty(_config.CreateParams.Name))
      {
        // Since filter on docker is only prefix filter
        var existing =
          host.Value.GetContainers(true, $"name={_config.CreateParams.Name}")
            .FirstOrDefault(x => IsNameMatch(x.Name, _config.CreateParams.Name));

        if (null != existing)
          return existing;
      }

      var container = host.Value.Create(_config.Image, _config.CreateParams, _config.StopOnDispose,
        _config.DeleteOnDispose,
        _config.DeleteVolumeOnDispose,
        _config.DeleteNamedVolumeOnDispose,
        _config.Command, _config.Arguments);

      AddHooks(container);

      foreach (var network in (IEnumerable<INetworkService>) _config.Networks ?? new INetworkService[0])
        network.Attach(container, true /*detachOnDisposeNetwork*/);

      if (null == _config.NetworkNames) return container;

      var nw = host.Value.GetNetworks();
      foreach (var network in (IEnumerable<string>) _config.NetworkNames ?? new string[0])
      {
        var nets = nw.First(x => x.Name == network);
        nets.Attach(container, true /*detachOnDisposeNetwork*/);
      }

      return container;
    }

    protected override IBuilder InternalCreate()
    {
      return new ContainerBuilder(this);
    }

    public ContainerBuilder RemoveVolumesOnDispose(bool includeNamedVolues = false)
    {
      _config.DeleteVolumeOnDispose = true;
      _config.DeleteNamedVolumeOnDispose = includeNamedVolues;
      return this;
    }

    public ContainerBuilder UseImage(string image)
    {
      _config.Image = image;
      return this;
    }

    public ContainerBuilder IsWindowsImage()
    {
      _config.IsWindowsImage = true;
      return this;
    }

    public ImageBuilder FromImage(string image)
    {
      UseImage(image);

      var builder = new ImageBuilder(this).AsImageName(image);
      Childs.Add(builder);

      return builder;
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
      _config.CreateParams.EnvironmentFiles = _config.CreateParams.EnvironmentFiles.ArrayAdd(file);
      return this;
    }

    public ContainerBuilder WithParentCGroup(int cgroup)
    {
      _config.CreateParams.ParentCGroup = cgroup.ToString();
      return this;
    }

    public ContainerBuilder UseCapability(params string[] capability)
    {
      _config.CreateParams.CapabilitiesToAdd = _config.CreateParams.CapabilitiesToAdd.ArrayAdd(capability);
      return this;
    }

    public ContainerBuilder RemoveCapability(params string[] capability)
    {
      _config.CreateParams.CapabilitiesToRemove = _config.CreateParams.CapabilitiesToRemove.ArrayAdd(capability);
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
        _config.CreateParams.HostIpMappings = new List<Tuple<string, IPAddress>>();

      _config.CreateParams.HostIpMappings.Add(new Tuple<string, IPAddress>(host, IPAddress.Parse(ip)));
      return this;
    }

    public ContainerBuilder ExposePort(int hostPort, int containerPort)
    {
      _config.CreateParams.PortMappings = _config.CreateParams.PortMappings.ArrayAdd($"{hostPort}:{containerPort}");
      return this;
    }

    public ContainerBuilder ExposePort(int containerPort)
    {
      _config.CreateParams.PortMappings = _config.CreateParams.PortMappings.ArrayAdd($"{containerPort}");
      return this;
    }

    public ContainerBuilder Mount(string fqHostPath, string fqContainerPath, MountType access)
    {
      var hp = OperatingSystem.IsWindows() && CommandExtensions.IsToolbox()
        ? ((TemplateString) fqHostPath).Rendered.ToMsysPath()
        : ((TemplateString) fqHostPath).Rendered;

      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.ArrayAdd($"{hp}:{fqContainerPath}:{access.ToDocker()}");
      return this;
    }

    public ContainerBuilder MountVolume(string name, string fqContainerPath, MountType access)
    {
      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.ArrayAdd($"{name}:{fqContainerPath}:{access.ToDocker()}");
      return this;
    }

    public ContainerBuilder MountVolume(IVolumeService volume, string fqContainerPath, MountType access)
    {
      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.ArrayAdd($"{volume.Name}:{fqContainerPath}:{access.ToDocker()}");
      return this;
    }

    public ContainerBuilder MountFrom(params string[] from)
    {
      _config.CreateParams.VolumesFrom = _config.CreateParams.VolumesFrom.ArrayAdd(from);
      return this;
    }

    public ContainerBuilder UseWorkDir(string workingDirectory)
    {
      _config.CreateParams.WorkingDirectory = workingDirectory;
      return this;
    }

    public ContainerBuilder Link(params string[] container)
    {
      _config.CreateParams.Links = _config.CreateParams.Links.ArrayAdd(container);
      return this;
    }

    public ContainerBuilder WithLabel(params string[] label)
    {
      _config.CreateParams.Labels = _config.CreateParams.Labels.ArrayAdd(label);
      return this;
    }

    public ContainerBuilder UseGroup(params string[] group)
    {
      _config.CreateParams.Groups = _config.CreateParams.Groups.ArrayAdd(group);
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

    public ContainerBuilder ReuseIfExists()
    {
      _config.VerifyExistence = true;
      return this;
    }

    /// <summary>
    ///   Uses a already pre-existing network service. It will automatically
    ///   detatch this container from the network when the network is disposed.
    /// </summary>
    /// <param name="network">The networks to attach this container to.</param>
    /// <returns>Itself for fluent access.</returns>
    public ContainerBuilder UseNetwork(params INetworkService[] network)
    {
      if (null == network || 0 == network.Length) return this;

      if (null == _config.Networks) _config.Networks = new List<INetworkService>();

      _config.Networks.AddRange(network);
      return this;
    }

    /// <summary>
    ///   Attaches to a network with specified name after the container has been created. It will automatically
    ///   detatch this container from the network when the network is disposed.
    /// </summary>
    /// <param name="network">The networks to attach this container to.</param>
    /// <returns>Itself for fluent access.</returns>
    public ContainerBuilder UseNetwork(params string[] network)
    {
      if (null == network || 0 == network.Length) return this;

      if (null == _config.NetworkNames) _config.NetworkNames = new List<string>();

      _config.NetworkNames.AddRange(network);
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
        _config.CpToOnStart = new List<Tuple<TemplateString, TemplateString>>();

      _config.CpToOnStart.Add(new Tuple<TemplateString, TemplateString>(hostPath, containerPath));
      return this;
    }

    public ContainerBuilder CopyOnDispose(string containerPath, string hostPath)
    {
      if (null == _config.CpFromOnDispose)
        _config.CpFromOnDispose = new List<Tuple<TemplateString, TemplateString>>();

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

    /// <summary>
    ///   Executes one or more commands including their arguments when container has started.
    /// </summary>
    /// <param name="execute">The binary to execute including any arguments to pass to the binary.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   Each execute string is respected as a binary and argument.
    /// </remarks>
    public ContainerBuilder ExecuteOnRunning(params string[] execute)
    {
      if (null == _config.ExecuteOnRunningArguments) _config.ExecuteOnRunningArguments = new List<string>();

      _config.ExecuteOnRunningArguments.AddRange(execute);
      return this;
    }

    /// <summary>
    ///   Executes one or more commands including their arguments when container about to stop.
    /// </summary>
    /// <param name="execute">The binary to execute including any arguments to pass to the binary.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   Each execute string is respected as a binary and argument.
    /// </remarks>
    public ContainerBuilder ExecuteOnDisposing(params string[] execute)
    {
      if (null == _config.ExecuteOnDisposingArguments) _config.ExecuteOnDisposingArguments = new List<string>();

      _config.ExecuteOnDisposingArguments.AddRange(execute);
      return this;
    }

    private void AddHooks(IService container)
    {
      // Copy files just before starting
      if (null != _config.CpToOnStart)
        container.AddHook(ServiceRunningState.Starting,
          service =>
          {
            foreach (var copy in _config.CpToOnStart)
              ((IContainerService) service).CopyTo(copy.Item2, copy.Item1);
          });

      // Wait for port when started
      if (null != _config.WaitForPort)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            ((IContainerService) service).WaitForPort(_config.WaitForPort.Item1, _config.WaitForPort.Item2);
          });

      // Wait for process when started
      if (null != _config.WaitForProcess)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            ((IContainerService) service).WaitForProcess(_config.WaitForProcess.Item1, _config.WaitForProcess.Item2);
          });

      // docker execute on running
      if (null != _config.ExecuteOnRunningArguments && _config.ExecuteOnRunningArguments.Count > 0)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          var svc = (IContainerService) service;
          foreach (var binaryAndArguments in _config.ExecuteOnRunningArguments)
          {
            var result = svc.DockerHost.Execute(svc.Id, binaryAndArguments, svc.Certificates);
            if (!result.Success)
              throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
          }
        });

      // Copy files / folders on dispose
      if (null != _config.CpFromOnDispose && 0 != _config.CpFromOnDispose.Count)
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          foreach (var copy in _config.CpFromOnDispose)
            ((IContainerService) service).CopyFrom(copy.Item2, copy.Item1);
        });

      // docker execute when disposing
      if (null != _config.ExecuteOnDisposingArguments && _config.ExecuteOnDisposingArguments.Count > 0)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          var svc = (IContainerService) service;
          foreach (var binaryAndArguments in _config.ExecuteOnDisposingArguments)
          {
            var result = svc.DockerHost.Execute(svc.Id, binaryAndArguments, svc.Certificates);
            if (!result.Success)
              throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
          }
        });

      // Export container on dispose
      if (null != _config.ExportOnDispose)
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          var svc = (IContainerService) service;
          if (_config.ExportOnDispose.Item3(svc))
            svc.Export(_config.ExportOnDispose.Item1,
              _config.ExportOnDispose.Item2);
        });
    }

    private static bool IsNameMatch(string containerName, string test)
    {
      return Regex.IsMatch(containerName, $@"^\/?{test}$");
    }
  }
}