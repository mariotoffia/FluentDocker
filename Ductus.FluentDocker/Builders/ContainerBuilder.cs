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
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ContainerBuilder : BaseBuilder<IContainerService>
  {
    private readonly ContainerBuilderConfig _config = new ContainerBuilderConfig();
    private RepositoryBuilder _repositoryBuilder;

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

          // Run hooks will not be run since they have already met the requirements
          // (since container was found running).
          AddHooks(existing);

          return existing;
        }
      }

      var firstNetwork = FindFirstNetworkNameAndAlias();

      if (string.Empty != firstNetwork.Network)
      {
        _config.CreateParams.Network = firstNetwork.Network;

        if(string.Empty != firstNetwork.Alias)
        {
          _config.CreateParams.Alias = firstNetwork.Alias;
        }
      }

      var container = host.Value.Create(_config.Image, _config.ImageFocrePull, _config.CreateParams, _config.StopOnDispose,
        _config.DeleteOnDispose,
        _config.DeleteVolumeOnDispose,
        _config.DeleteNamedVolumeOnDispose,
        _config.Command, _config.Arguments);

      AddHooks(container);

      foreach (var network in (IEnumerable<INetworkService>)_config.Networks ?? Array.Empty<INetworkService>())
      {
        if (network.Name != firstNetwork.Network)
          network.Attach(container, true /*detachOnDisposeNetwork*/);
      }

      foreach (var networkWithAlias in (IEnumerable<NetworkWithAlias<INetworkService>>)_config.NetworksWithAlias ?? Array.Empty<NetworkWithAlias<INetworkService>>())
      {
        var network = networkWithAlias.Network;
        if (network.Name != firstNetwork.Network)
          network.Attach(container, true /*detachOnDisposeNetwork*/, networkWithAlias.Alias);
      }

      if (null == _config.NetworkNames)
        return container;

      var nw = host.Value.GetNetworks();
      foreach (var network in (IEnumerable<string>)_config.NetworkNames ?? Array.Empty<string>())
      {
        if (network == firstNetwork.Network)
          continue;

        var nets = nw.First(x => x.Name == network);
        nets.Attach(container, true /*detachOnDisposeNetwork*/);
      }

      foreach (var networkWithAlias in (IEnumerable<NetworkWithAlias<string>>)_config.NetworkNamesWithAlias ?? Array.Empty<NetworkWithAlias<string>>())
      {
        var network = networkWithAlias.Network;
        if (network == firstNetwork.Network)
          continue;

        var nets = nw.First(x => x.Name == network);
        nets.Attach(container, true /*detachOnDisposeNetwork*/, networkWithAlias.Alias);
      }

      return container;
    }

    private NetworkWithAlias<string> FindFirstNetworkNameAndAlias()
    {

      if(_config.Networks != null && _config.Networks.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = _config.Networks[0].Name,
        };
      }
      else if (_config.NetworksWithAlias != null && _config.NetworksWithAlias.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = _config.NetworksWithAlias[0].Network.Name,
          Alias = _config.NetworksWithAlias[0].Alias
        };
      }
      else if (_config.NetworkNames != null && _config.NetworkNames.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = _config.NetworkNames[0],
        };
      }
      else if (_config.NetworkNamesWithAlias != null && _config.NetworkNamesWithAlias.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = _config.NetworkNamesWithAlias[0].Network,
          Alias = _config.NetworkNamesWithAlias[0].Alias
        };
      }

      return new NetworkWithAlias<string>
      {
        Network = string.Empty,
        Alias = string.Empty
      };
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

    public ContainerBuilder UseImage(string image, bool force = false)
    {
      _config.Image = image;
      _config.ImageFocrePull = force;
      return this;
    }

    /// <summary>
    ///   Uses credentials to login to a registry.
    /// </summary>
    /// <param name="server">The ip or dns to the server (with optioanl :port)</param>
    /// <param name="user">An optional user to use when logging in.</param>
    /// <param name="password">An optional password to user when logging in.</param>
    /// <returns>Itself for fluent access.</returns>
    public ContainerBuilder WithCredential(string server, string user = null, string password = null)
    {
      _repositoryBuilder = new RepositoryBuilder(server, user, password);
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

    public ContainerBuilder WithHostName(string name)
    {
      _config.CreateParams.Hostname = name;
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

    public ContainerBuilder UseDns(params string[] server)
    {
      _config.CreateParams.Dns = _config.CreateParams.Dns.ArrayAdd(server);
      return this;
    }

    public ContainerBuilder UseDnsSearch(params string[] searchArg)
    {
      _config.CreateParams.DnsSearch = _config.CreateParams.DnsSearch.ArrayAdd(searchArg);
      return this;
    }

    public ContainerBuilder UseDnsOption(params string[] option)
    {
      _config.CreateParams.DnsOpt = _config.CreateParams.DnsOpt.ArrayAdd(option);
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

    /// <summary>
    /// Deprecated ue <see cref="UseHealthCheck(string, string, string, int)"/> instead.
    /// </summary>
    /// <param name="cmd">Commnad to use in the health check.</param>
    /// <returns>Itself for fluent access</returns>
    [Deprecated("Will be removed since replaced by UseHealthCheck")]
    public ContainerBuilder HealthCheck(string cmd)
    {
      return UseHealthCheck(cmd);
    }

    /// <summary>
    /// Completely disable HEALTHCHECK.
    /// </summary>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// Independant on what is specified in the Dockerfile (in HEALTHCHECK section).
    /// All is disabled!
    /// </remarks>
    public ContainerBuilder UseNoHealthCheck()
    {
      _config.CreateParams.HealthCheckDisabled = true;
      return this;
    }

    /// <summary>
    /// Sets or overrides the native HEALTHCHECK provided by the docker daemon.
    /// </summary>
    /// <param name="cmd">A commnad to preform the health check.</param>
    /// <param name="interval">How ofthen to perform the <paramref name="cmd"/>, default is 30s.</param>
    /// <param name="timeout">How long time the <paramref name="cmd"/> has in order to not be marked as unhealthy container.</param>
    /// <param name="startPeriod">How long for the first execution of <paramref name="cmd"/>, default is 30s.</param>
    /// <param name="retries">The number of retries a <paramref name="cmd"/> has in order for the container do be marked as unhealthy, default is 3.</param>
    /// <returns>Itself for fluent access.</returns>
    public ContainerBuilder UseHealthCheck(string cmd = null, string interval = null, string timeout = null, string startPeriod = null, int retries = -1)
    {
      if (!string.IsNullOrEmpty(cmd))
        _config.CreateParams.HealthCheckCmd = cmd;

      if (!string.IsNullOrEmpty(interval))
        _config.CreateParams.HealthCheckInterval = interval;

      if (!string.IsNullOrEmpty(timeout))
        _config.CreateParams.HealthCheckTimeout = timeout;

      if (!string.IsNullOrEmpty(startPeriod))
        _config.CreateParams.HealthCheckStartPeriod = startPeriod;

      if (retries > 0)
        _config.CreateParams.HealthCheckRetries = retries;

      return this;
    }

    public ContainerBuilder WithIPC(string ipc)
    {
      _config.CreateParams.Ipc = ipc;
      return this;
    }

    /// <summary>
    /// Specifies a container runtime, e.g. NVIDIA for GPU workloads.
    /// </summary>
    /// <param name="runtime">A runtime to execute the container under.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// By default, containers execute under docker provided default environment
    /// <see cref="ContainerRuntime.Default"/>. It is not neccesary to specify
    /// such. Instead, if a custom runtime is wanted for the container specify custom
    /// runtime such as <see cref="ContainerRuntime.Nvidia"/>.
    /// </remarks>
    public ContainerBuilder UseRuntime(ContainerRuntime runtime)
    {
      _config.CreateParams.Runtime = runtime;
      return this;
    }

    public ContainerBuilder Mount(string fqHostPath, string fqContainerPath, MountType access)
    {
      var hp = FdOs.IsWindows() && CommandExtensions.IsToolbox()
        ? ((TemplateString)fqHostPath).Rendered.ToMsysPath()
        : ((TemplateString)fqHostPath).Rendered;

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
    ///   detach this container from the network when the network is disposed.
    /// </summary>
    /// <param name="network">The networks to attach this container to.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// The first parameter will not be used to do a docker network attach. Instead
    /// it is used as a creation parameter --network. This is to support static ip
    /// assignment. The rest of the networks will do attach via docker network.
    /// </remarks>
    public ContainerBuilder UseNetwork(params INetworkService[] network)
    {
      if (null == network || 0 == network.Length)
        return this;

      if (null == _config.Networks)
        _config.Networks = new List<INetworkService>();

      _config.Networks.AddRange(network);
      return this;
    }

    /// <summary>
    ///   Uses a already pre-existing network service. It will automatically
    ///   detach this container from the network when the network is disposed.
    /// </summary>
    /// <param name="alias">The alias to use for the container in each of the given networks</param>
    /// <param name="networks">The networks to attach this container to.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// The first parameter will not be used to do a docker network attach if no <see cref="UseNetwork(INetworkService[])"/>
    /// is set (those have precedence).
    /// assignment. The rest of the networks will do attach via docker network.
    /// </remarks>
    public ContainerBuilder UseNetworksWithAlias(string alias, params INetworkService[] networks)
    {
      _ = alias ?? throw new ArgumentNullException(nameof(alias));

      if (null == networks || 0 == networks.Length)
        return this;

      if (null == _config.Networks)
        _config.NetworksWithAlias = new List<NetworkWithAlias<INetworkService>>();


      foreach(var network in networks)
      {
        _config.NetworksWithAlias.Add(new NetworkWithAlias<INetworkService>
          {
            Network = network,
            Alias = alias
          });
      }

      return this;
    }

    /// <summary>
    ///   Attaches to a network with specified name after the container has been created. It will automatically
    ///   detach this container from the network when the network is disposed.
    /// </summary>
    /// <param name="network">The networks to attach this container to.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// The first network from will be used as docker create --network parameter, if no <see cref="UseNetworksWithAlias(string, INetworkService[])"/>
    /// is set (those have precedence).  This is to support static ip
    /// assignment. The rest of the networks will do attach via docker network.
    /// </remarks>
    public ContainerBuilder UseNetwork(params string[] network)
    {
      if (null == network || 0 == network.Length)
        return this;

      if (null == _config.NetworkNames)
        _config.NetworkNames = new List<string>();

      _config.NetworkNames.AddRange(network);
      return this;
    }

    /// <summary>
    ///   Attaches to a network with specified name after the container has been created. It will automatically
    ///   detach this container from the network when the network is disposed.
    /// </summary>
    /// <param name="alias">The alias to use for the container in each of the given networks</param>
    /// <param name="networks">The networks to attach this container to.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// The first network from will be used as docker create --network parameter, if no <see cref="UseNetwork(INetworkService[])"/>
    /// is set (those have precedence).  This is to support static ip
    /// assignment. The rest of the networks will do attach via docker network.
    /// </remarks>
    public ContainerBuilder UseNetworksWithAlias(string alias, params string[] networks)
    {
      _ = alias ?? throw new ArgumentNullException(nameof(alias));

      if (null == networks || 0 == networks.Length)
        return this;

      if (null == _config.NetworkNames)
        _config.NetworkNamesWithAlias = new List<NetworkWithAlias<string>>();

      foreach (var network in networks)
      {
        _config.NetworkNamesWithAlias.Add(new NetworkWithAlias<string>
        {
          Network = network,
          Alias = alias
        });
      }

      return this;
    }

    /// <summary>
    ///   Set the container Ip explicitly.
    /// </summary>
    /// <param name="ipv4">A ip v4 e.g. 1.1.1.1</param>
    /// <returns>Itself for fluent access.</returns>
    public ContainerBuilder UseIpV4(string ipv4)
    {
      _config.CreateParams.Ipv4 = ipv4;
      return this;
    }

    /// <summary>
    ///   Set the container Ip explicitly.
    /// </summary>
    /// <param name="ipv6">A ip v6 e.g. 2001:db8::33</param>
    /// <returns>Itself for fluent access.</returns>
    public ContainerBuilder UseIpV6(string ipv6)
    {
      _config.CreateParams.Ipv6 = ipv6;
      return this;
    }

    /// <summary>
    /// Adds a new <see cref="Ulimit"/> onto a container.
    /// </summary>
    /// <param name="ulimit">The ulimit to impose.</param>
    /// <param name="soft">The soft value.</param>
    /// <param name="hard">The optional hard value.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///  For example restricting the number of open files to 10 use <see cref="Ulimit.NoFile"/> and set soft / hard
    /// to 10. 
    /// </remarks>
    public ContainerBuilder UseUlimit(Ulimit ulimit, string soft, string hard = null)
    {
      _config.CreateParams.Ulimit.Add(new ULimitItem(ulimit, soft, hard));
      return this;
    }

    /// <summary>
    /// Adds a new <see cref="Ulimit"/> onto a container.
    /// </summary>
    /// <param name="ulimit">The ulimit to impose.</param>
    /// <param name="soft">The soft value.</param>
    /// <param name="hard">The optional hard value.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///  For example restricting the number of open files to 10 use <see cref="Ulimit.NoFile"/> and set soft / hard
    /// to 10. 
    /// </remarks>
    public ContainerBuilder UseUlimit(Ulimit ulimit, long soft, long? hard = null)
    {
      _config.CreateParams.Ulimit.Add(new ULimitItem(ulimit, soft.ToString(), hard.HasValue ? hard.ToString() : null));
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

    public ContainerBuilder WaitForPort(string portAndProto, double millisTimeout = double.MaxValue, string address = null)
    {
      if (millisTimeout >= long.MaxValue)
        millisTimeout = long.MaxValue;

      _config.WaitForPort = new Tuple<string, string, long>(portAndProto, address, Convert.ToInt64(millisTimeout));
      return this;
    }

    public ContainerBuilder WaitForMessageInLog(string message, TimeSpan timeout = default)
    {
      if (timeout == default)
        timeout = TimeSpan.MaxValue;

      _config.WaitForMessageInLog = new Tuple<long, string>((long)timeout.TotalMilliseconds, message);
      return this;
    }

    /// <summary>
    /// This install a wait for a docker daemon HEALTH check until certain timeout to set to Healthy.
    /// </summary>
    /// <param name="timeout">A optional timeout to stop waiting.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   When the container is in <see cref="ServiceRunningState.Running"/> mode it will poll
    ///   the container configuration and check the health of the container (as classified by the
    ///   docker daemon). When the container is reported as Healthy, this method silently exits.
    ///   If timeout a <see cref="FluentDockerException"/> is thrown. The status is deemed
    ///   by the containers Dockerfiles HEALTH section or if overrided / created by
    ///   <see cref="UseHealthCheck(string, string, string, string, int)"/>.
    /// </remarks>
    public ContainerBuilder WaitForHealthy(TimeSpan timeout = default)
    {
      if (timeout == default)
        timeout = TimeSpan.MaxValue;

      _config.WaitForHealthy = new Tuple<long>(Convert.ToInt64(timeout.TotalMilliseconds));
      return this;
    }

    public ContainerBuilder WaitForPort(string portAndProto, TimeSpan timeout = default, string address = null)
    {
      if (timeout == default)
        timeout = TimeSpan.FromMilliseconds(long.MaxValue);

      _config.WaitForPort = new Tuple<string, string, long>(portAndProto, address, Convert.ToInt64(timeout.TotalMilliseconds));
      return this;
    }

    public ContainerBuilder WaitForPort(string portAndProto, long millisTimeout = long.MaxValue, string address = null)
    {
      _config.WaitForPort = new Tuple<string, string, long>(portAndProto, address, millisTimeout);
      return this;
    }

    /// <summary>
    ///   Custom function to do verification if wait is over or not.
    /// </summary>
    /// <param name="continuation">The continuation lambda.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    ///   It is possible to stack multiple lambdas, they are executed in order they where registered (per service).
    ///   The lambda do the actual action to determine if the wait is over or not. If it returns zero or less, the
    ///   wait is over. If it returns a positive value, the wait function will wait this amount of milliseconds before
    ///   invoking it again. The second argument is the invocation count. This can be used for the function to determine
    ///   any type of abort action due to the amount of invocations. If continuation wishes to abort, it shall throw
    ///   <see cref="FluentDockerException" />.
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
        Url = url,
        Timeout = timeout,
        Continuation = continuation,
        Method = method,
        ContentType = contentType,
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
      if (null == _config.ExecuteOnRunningArguments)
        _config.ExecuteOnRunningArguments = new List<string>();

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
      if (null == _config.ExecuteOnDisposingArguments)
        _config.ExecuteOnDisposingArguments = new List<string>();

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
                ((IContainerService)service).CopyTo(copy.Item2, copy.Item1);
            }, service, "Copy on start");
          });

      // Wait for port when started
      if (null != _config.WaitForPort)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            Fd.DisposeOnException(svc =>
                ((IContainerService)service).WaitForPort(_config.WaitForPort.Item1, _config.WaitForPort.Item3,
                  _config.WaitForPort.Item2),
              service, "Wait for port");
          });

      // Wait for healthy when started
      if (null != _config.WaitForHealthy)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            Fd.DisposeOnException(svc =>
                ((IContainerService)service).WaitForHealthy(_config.WaitForHealthy.Item1),
              service, "Wait for healthy");
          });

      // Wait for http when started
      if (null != _config.WaitForHttp && 0 != _config.WaitForHttp.Count)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            foreach (var prm in _config.WaitForHttp)
              ((IContainerService)service).WaitForHttp(prm.Url, prm.Timeout, prm.Continuation, prm.Method,
                prm.ContentType,
                prm.Body);
          }, service, "Wait for HTTP");
        });

      // Wait for process when started
      if (null != _config.WaitForProcess)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            Fd.DisposeOnException(src =>
                ((IContainerService)service).WaitForProcess(_config.WaitForProcess.Item1,
                  _config.WaitForProcess.Item2),
              service, "Wait for process");
          });

      // Wait for message in log when started
      if (null != _config.WaitForMessageInLog)
        container.AddHook(ServiceRunningState.Running,
          service =>
          {
            Fd.DisposeOnException(src =>
                ((IContainerService)service).WaitForMessageInLogs(_config.WaitForMessageInLog.Item2,
                  _config.WaitForMessageInLog.Item1),
              service, "Wait for process");
          });

      // Wait for lambda when started
      if (null != _config.WaitLambda && 0 != _config.WaitLambda.Count)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          Fd.DisposeOnException(src =>
          {
            foreach (var continuation in _config.WaitLambda)
              ((IContainerService)service).Wait(continuation);
          }, service, "Wait for lambda");
        });

      // docker execute on running
      if (null != _config.ExecuteOnRunningArguments && _config.ExecuteOnRunningArguments.Count > 0)
        container.AddHook(ServiceRunningState.Running, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            var csvc = (IContainerService)service;
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
              ((IContainerService)service).CopyFrom(copy.Item2, copy.Item1);
          }, service, "Copy From on Dispose");
        });

      // docker execute when disposing
      if (null != _config.ExecuteOnDisposingArguments && _config.ExecuteOnDisposingArguments.Count > 0)
        container.AddHook(ServiceRunningState.Removing, service =>
        {
          Fd.DisposeOnException(svc =>
          {
            var csvc = (IContainerService)service;
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
            var csvc = (IContainerService)service;
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
