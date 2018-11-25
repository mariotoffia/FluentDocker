using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ContainerBuilder : BaseBuilder<IContainerService>
  {
    private readonly ContainerBuilderConfig _config = new ContainerBuilderConfig();
    private RepositoryBuilder _repositoryBuilder = null;
    
    internal ContainerBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IContainerService Build()
    {
      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build container {_config.Image} since no host service is defined");

      // Login on private repo if needed.
      _repositoryBuilder?.Build(host.Value);

      if (_config.VerifyExistence && !string.IsNullOrEmpty(_config.CreateParams.Name))
      {
        // Since filter on docker is only prefix filter
        var existing =
          host.Value.GetContainers(true, $"name={_config.CreateParams.Name}")
            .FirstOrDefault(x => IsNameMatch(x.Name, _config.CreateParams.Name));

        if (null != existing)
        {
          existing.RemoveOnDispose = _config.DeleteOnDispose;
          existing.StopOnDispose = _config.StopOnDispose;

          return existing;
        }
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

    public ContainerBuilder WithCredential(string user, string password)
    {
      _repositoryBuilder = new RepositoryBuilder(user: user, pass: password);
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

    public CompositeBuilder UseCompose()
    {
      var builder = new CompositeBuilder(this);
      Childs.Add(builder);
      return builder;
    }

    public CompositeBuilder FromComposeFile(string composeFile)
    {
      return UseCompose().FromFile(composeFile);
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

    public ContainerBuilder IsPrivileged()
    {
      _config.CreateParams.Privileged = true;
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
      var hp = FdOs.IsWindows() && CommandExtensions.IsToolbox()
        ? ((TemplateString) fqHostPath).Rendered.ToMsysPath()
        : ((TemplateString) fqHostPath).Rendered;

      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.ArrayAdd($"{hp.EscapePath()}:{fqContainerPath.EscapePath()}:{access.ToDocker()}");
      return this;
    }

    public ContainerBuilder MountVolume(string name, string fqContainerPath, MountType access)
    {
      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.ArrayAdd($"{name}:{fqContainerPath.EscapePath()}:{access.ToDocker()}");
      return this;
    }

    public ContainerBuilder MountVolume(IVolumeService volume, string fqContainerPath, MountType access)
    {
      _config.CreateParams.Volumes =
        _config.CreateParams.Volumes.ArrayAdd($"{volume.Name}:{fqContainerPath.EscapePath()}:{access.ToDocker()}");
      return this;
    }

    public ContainerBuilder MountFrom(params string[] from)
    {
      _config.CreateParams.VolumesFrom = _config.CreateParams.VolumesFrom.ArrayAdd(from);
      return this;
    }

    public ContainerBuilder UseWorkDir(string workingDirectory)
    {
      _config.CreateParams.WorkingDirectory = workingDirectory.EscapePath();
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
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath.EscapePath(), false /*no-explode*/,
          condition ?? (svc => true));
      return this;
    }

    public ContainerBuilder ExportExploadedOnDispose(string hostPath, Func<IContainerService, bool> condition = null)
    {
      _config.ExportOnDispose =
        new Tuple<TemplateString, bool, Func<IContainerService, bool>>(hostPath.EscapePath(), true /*explode*/,
          condition ?? (svc => true));
      return this;
    }

    public ContainerBuilder CopyOnStart(string hostPath, string containerPath)
    {
      if (null == _config.CpToOnStart)
        _config.CpToOnStart = new List<Tuple<TemplateString, TemplateString>>();

      _config.CpToOnStart.Add(
        new Tuple<TemplateString, TemplateString>(hostPath.EscapePath(), containerPath.EscapePath()));
      return this;
    }

    public ContainerBuilder CopyOnDispose(string containerPath, string hostPath)
    {
      if (null == _config.CpFromOnDispose)
        _config.CpFromOnDispose = new List<Tuple<TemplateString, TemplateString>>();

      _config.CpFromOnDispose.Add(new Tuple<TemplateString, TemplateString>(hostPath.EscapePath(),
        containerPath.EscapePath()));
      return this;
    }

    public ContainerBuilder WaitForPort(string portAndProto, double millisTimeout = double.MaxValue)
    {
      if (millisTimeout >= long.MaxValue)
        millisTimeout = long.MaxValue;
      
      _config.WaitForPort = new Tuple<string, long>(portAndProto, Convert.ToInt64(millisTimeout));
      return this;
    }

    public ContainerBuilder WaitForPort(string portAndProto, TimeSpan timeout = default(TimeSpan))
    {
      if (timeout == default(TimeSpan)) timeout = TimeSpan.FromMilliseconds(long.MaxValue);

      _config.WaitForPort = new Tuple<string, long>(portAndProto, Convert.ToInt64(timeout.TotalMilliseconds));
      return this;
    }

    public ContainerBuilder WaitForPort(string portAndProto, long millisTimeout = long.MaxValue)
    {
      _config.WaitForPort = new Tuple<string, long>(portAndProto, millisTimeout);
      return this;
    }
    
    /// <summary>
    /// Custom function to do verification if wait is over or not.
    /// </summary>
    /// <param name="continuation">The continuation lambda.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// It is possible to stack multiple lambdas, they are executed in order they where registered (per service).
    /// The lambda do the actual action to determine if the wait is over or not. If it returns zero or less, the
    /// wait is over. If it returns a positive value, the wait function will wait this amount of milliseconds before
    /// invoking it again. The second argument is the invocation count. This can be used for the function to determine
    /// any type of abort action due to the amount of invocations. If continuation wishes to abort, it shall throw
    /// <see cref="FluentDockerException"/>.
    /// </remarks>
    public ContainerBuilder Wait(string service, Func<IContainerService, int, int> continuation)
    {
      _config.WaitLambda.Add(continuation);
      return this;
    }
    
    /// <summary>
    ///   Waits for a request to be passed or failed.
    /// </summary>
    /// <param name="url">The url including any query parameters.</param>
    /// <param name="continuation">Optional continuation that evaluates if it shall still wait or continue.</param>
    /// <param name="method">Optional. The method. Default is <see cref="HttpMethod.Get" />.</param>
    /// <param name="contentType">Optional. The content type in put, post operations. Defaults to application/json</param>
    /// <param name="body">Optional. A body to post or put.</param>
    /// <returns>The response body in form of a string.</returns>
    /// <exception cref="ArgumentException">If <paramref name="method" /> is not GET, PUT, POST or DELETE.</exception>
    /// <exception cref="HttpRequestException">If any errors during the HTTP request.</exception>
    public ContainerBuilder WaitForHttp(string url, long timeout = 60_000,
      Func<RequestResponse, int, long> continuation = null, HttpMethod method = null,
      string contentType = "application/json", string body = null)
    {
      _config.WaitForHttp.Add(new ContainerSpecificConfig.WaitForHttpParams
      {
        Url = url, Timeout = timeout, Continuation = continuation, Method = method, ContentType = contentType,
        Body = body
      });

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
            Fd.DisposeOnException(svc =>
            {
              foreach (var copy in _config.CpToOnStart)
                ((IContainerService) service).CopyTo(copy.Item2, copy.Item1);
            }, service, "Copy on start");
          });

      // Wait for port when started
      if (null != _config.WaitForPort)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            Fd.DisposeOnException(svc =>
                ((IContainerService) service).WaitForPort(_config.WaitForPort.Item1, _config.WaitForPort.Item2),
              service, "Wait for port");
          });

      // Wait for http when started
      if (null != _config.WaitForHttp && 0 != _config.WaitForHttp.Count)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            foreach (var prm in _config.WaitForHttp)
              ((IContainerService) service).WaitForHttp(prm.Url, prm.Timeout, prm.Continuation, prm.Method,
                prm.ContentType,
                prm.Body);
          }, service, "Wait for HTTP");
        });

      // Wait for lambda when started
      if (null != _config.WaitLambda && 0 != _config.WaitLambda.Count)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          Fd.DisposeOnException(src =>
          {
            foreach (var continuation in _config.WaitLambda)
              ((IContainerService) service).Wait(continuation);
          }, service, "Wait for lambda");
        });


      // Wait for process when started
      if (null != _config.WaitForProcess)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            Fd.DisposeOnException(src =>
                ((IContainerService) service).WaitForProcess(_config.WaitForProcess.Item1,
                  _config.WaitForProcess.Item2),
              service, "Wait for process");
          });

      // docker execute on running
      if (null != _config.ExecuteOnRunningArguments && _config.ExecuteOnRunningArguments.Count > 0)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            var csvc = (IContainerService) service;
            foreach (var binaryAndArguments in _config.ExecuteOnRunningArguments)
            {
              var result = csvc.DockerHost.Execute(csvc.Id, binaryAndArguments, csvc.Certificates);
              if (!result.Success)
                throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
            }
          }, service, "Execute On Running Arguments");
        });

      // Copy files / folders on dispose
      if (null != _config.CpFromOnDispose && 0 != _config.CpFromOnDispose.Count)
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            foreach (var copy in _config.CpFromOnDispose)
              ((IContainerService) service).CopyFrom(copy.Item2, copy.Item1);
          }, service, "Copy From on Dispose");
        });

      // docker execute when disposing
      if (null != _config.ExecuteOnDisposingArguments && _config.ExecuteOnDisposingArguments.Count > 0)
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            var csvc = (IContainerService) service;
            foreach (var binaryAndArguments in _config.ExecuteOnDisposingArguments)
            {
              var result = csvc.DockerHost.Execute(csvc.Id, binaryAndArguments, csvc.Certificates);
              if (!result.Success)
                throw new FluentDockerException($"Failed to execute {binaryAndArguments} error: {result.Error}");
            }
          }, service, "Execute On Disposing Argument");
        });

      // Export container on dispose
      if (null != _config.ExportOnDispose)
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            var csvc = (IContainerService) service;
            if (_config.ExportOnDispose.Item3(csvc))
              csvc.Export(_config.ExportOnDispose.Item1,
                _config.ExportOnDispose.Item2);
          }, service, "Export on Dispose");
        });
    }

    private static bool IsNameMatch(string containerName, string test)
    {
      return Regex.IsMatch(containerName, $@"^\/?{test}$");
    }
  }
}