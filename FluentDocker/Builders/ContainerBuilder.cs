using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Container builder implementation.
  /// </summary>
  internal sealed partial class ContainerBuilder : IContainerBuilder, IDriverScopedBuilder
  {
    private static readonly char[] EqualsSeparator = ['='];
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private string _image;
    private string _name;
    private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _ports = new Dictionary<string, string>();
    private readonly List<string> _command = new List<string>();
    private readonly Dictionary<string, string> _volumes = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
    private readonly List<string> _networks = new List<string>();
    private readonly List<NetworkAlias> _networkAliases = new List<NetworkAlias>();
    private readonly List<WaitCondition> _waitConditions = new List<WaitCondition>();
    private readonly List<LifecycleHook> _lifecycleHooks = new List<LifecycleHook>();
    private readonly List<ContainerLink> _links = new List<ContainerLink>();

    private string _workingDir;
    private string _user;
    private string _restartPolicy;
    private string _hostname;
    private string _networkMode;
    private string _ipv4Address;
    private string _ipv6Address;
    private long? _memoryLimit;
    private long? _cpuShares;
    private bool _privileged;
    private bool _autoRemove;
    private bool _keepContainer;
    private bool _keepRunning;
    private bool _deleteVolumeOnDispose;
    private bool _deleteNamedVolumeOnDispose;
    private bool _forcePullImage;
    private ContainerExistsBehavior _existsBehavior = ContainerExistsBehavior.Default;
    private bool _destroyForce;
    private bool _destroyRemoveVolumes;
    private Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> _customResolver;
    private string _pod;
    private readonly List<string> _capAdd = new();
    private readonly List<string> _capDrop = new();
    private readonly List<string> _securityOpt = new();
    private long? _shmSize;
    private readonly Dictionary<string, string> _tmpfs = new();
    private readonly Dictionary<string, string> _devices = new();
    private bool _readonlyRootfs;
    private string _platform;
    private string _runtime;
    private int _waitPollIntervalMs = 500;
    private Services.Impl.ContainerService _pendingService;
    private bool _waitConditionsExecuted;

    public ContainerBuilder(FluentDockerKernel kernel, string driverId)
    {
      _kernel = kernel;
      _driverId = driverId;
    }

    #region Basic Configuration

    public IContainerBuilder UseImage(string image) { _image = image; return this; }
    public IContainerBuilder WithName(string name) { _name = name; return this; }

    public IContainerBuilder WithEnvironment(string key, string value)
    {
      _environment[key] = value;
      return this;
    }

    public IContainerBuilder WithEnvironment(string keyValue)
    {
      var parts = keyValue.Split(EqualsSeparator, 2);
      if (parts.Length == 2)
        _environment[parts[0]] = parts[1];
      else
        _environment[keyValue] = string.Empty;
      return this;
    }

    public IContainerBuilder WithPort(string containerPort, string hostPort)
    {
      _ports[containerPort] = hostPort;
      return this;
    }

    public IContainerBuilder ExposePort(string containerPort)
    {
      var normalized = containerPort.Contains('/') ? containerPort : $"{containerPort}/tcp";
      _ports[normalized] = "";
      return this;
    }

    public IContainerBuilder ExposePort(int hostPort, int containerPort)
    {
      _ports[$"{containerPort}/tcp"] = hostPort.ToString();
      return this;
    }

    public IContainerBuilder WithCommand(params string[] command) { _command.AddRange(command); return this; }
    public IContainerBuilder WithVolume(string hostPath, string containerPath) { _volumes[hostPath] = containerPath; return this; }
    public IContainerBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
    public IContainerBuilder WithWorkingDirectory(string workingDir) { _workingDir = workingDir; return this; }
    public IContainerBuilder WithUser(string user) { _user = user; return this; }
    public IContainerBuilder WithRestartPolicy(string policy) { _restartPolicy = policy; return this; }
    public IContainerBuilder WithHostname(string hostname) { _hostname = hostname; return this; }
    public IContainerBuilder WithNetworkMode(string networkMode) { _networkMode = networkMode; return this; }
    public IContainerBuilder WithNetwork(string networkName) { _networks.Add(networkName); return this; }

    public IContainerBuilder WithNetworkAlias(string networkName, string alias)
    {
      _networkAliases.Add(new NetworkAlias { NetworkName = networkName, Alias = alias });
      if (!_networks.Contains(networkName))
        _networks.Add(networkName);
      return this;
    }

    public IContainerBuilder WithIPv4(string ipv4Address) { _ipv4Address = ipv4Address; return this; }
    public IContainerBuilder WithIPv6(string ipv6Address) { _ipv6Address = ipv6Address; return this; }
    public IContainerBuilder WithMemoryLimit(long bytes) { _memoryLimit = bytes; return this; }
    public IContainerBuilder WithCpuShares(long shares) { _cpuShares = shares; return this; }
    public IContainerBuilder WithPrivileged(bool privileged = true) { _privileged = privileged; return this; }
    public IContainerBuilder WithAutoRemove(bool autoRemove = true) { _autoRemove = autoRemove; return this; }

    public IContainerBuilder WithLink(string containerName, string alias = null)
    {
      _links.Add(new ContainerLink { ContainerName = containerName, Alias = alias ?? containerName });
      return this;
    }

    public IContainerBuilder WithLinks(params string[] containerNames)
    {
      foreach (var name in containerNames)
        _links.Add(new ContainerLink { ContainerName = name, Alias = name });
      return this;
    }

    public IContainerBuilder WithPod(string podName) { _pod = podName; return this; }
    public IContainerBuilder WithCapAdd(string capability) { _capAdd.Add(capability); return this; }
    public IContainerBuilder WithCapDrop(string capability) { _capDrop.Add(capability); return this; }
    public IContainerBuilder WithSecurityOpt(string option) { _securityOpt.Add(option); return this; }
    public IContainerBuilder WithShmSize(long bytes) { _shmSize = bytes; return this; }
    public IContainerBuilder WithTmpfs(string containerPath, string options = null) { _tmpfs[containerPath] = options ?? ""; return this; }
    public IContainerBuilder WithDevice(string hostDevice, string containerDevice = null) { _devices[hostDevice] = containerDevice ?? hostDevice; return this; }
    public IContainerBuilder WithReadonlyRootfs() { _readonlyRootfs = true; return this; }
    public IContainerBuilder WithPlatform(string platform) { _platform = platform; return this; }
    public IContainerBuilder WithRuntime(string runtime) { _runtime = runtime; return this; }

    #endregion

    #region Container Existence Behavior

    public IContainerBuilder ReuseIfExists() { _existsBehavior = ContainerExistsBehavior.Reuse; return this; }

    public IContainerBuilder DestroyIfExists(bool force = false, bool removeVolumes = false)
    {
      _existsBehavior = ContainerExistsBehavior.Destroy;
      _destroyForce = force;
      _destroyRemoveVolumes = removeVolumes;
      return this;
    }

    public IContainerBuilder ForcePullImage() { _forcePullImage = true; return this; }

    #endregion

    #region Wait Conditions

    public IContainerBuilder WithWaitPollInterval(int intervalMs)
    {
      _waitPollIntervalMs = intervalMs;
      return this;
    }

    public IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Port,
        Target = portAndProto.Contains('/') ? portAndProto : $"{portAndProto}/tcp",
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Port,
        Target = portAndProto.Contains('/') ? portAndProto : $"{portAndProto}/tcp",
        Path = address,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Process,
        Target = processName,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Http,
        Target = portAndProto.Contains('/') ? portAndProto : $"{portAndProto}/tcp",
        Path = path,
        TimeoutMs = timeoutMs,
        HttpMethod = HttpMethod.Get,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHttp(string url, long timeoutMs = 30000,
        HttpMethod method = null, string contentType = null, string body = null,
        Func<RequestResponse, int, long> continuation = null)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Http,
        Target = url,
        TimeoutMs = timeoutMs,
        HttpMethod = method ?? HttpMethod.Get,
        ContentType = contentType,
        Body = body,
        HttpContinuation = continuation,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.LogMessage,
        Target = message,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder WaitForHealthy(long timeoutMs = 30000)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Healthy,
        TimeoutMs = timeoutMs,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    public IContainerBuilder Wait(Func<IContainerService, int, int> condition)
    {
      _waitConditions.Add(new WaitCondition
      {
        Type = WaitConditionType.Lambda,
        LambdaCondition = condition,
        TimeoutMs = 60000,
        PollIntervalMs = _waitPollIntervalMs
      });
      return this;
    }

    #endregion

    #region Dispose Behavior

    public IContainerBuilder KeepContainer() { _keepContainer = true; return this; }
    public IContainerBuilder KeepRunning() { _keepRunning = true; return this; }
    public IContainerBuilder DeleteVolumeOnDispose() { _deleteVolumeOnDispose = true; return this; }
    public IContainerBuilder DeleteNamedVolumeOnDispose() { _deleteNamedVolumeOnDispose = true; return this; }

    #endregion

    #region Advanced

    public IContainerBuilder UseCustomResolver(
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> resolver)
    {
      _customResolver = resolver;
      return this;
    }

    #endregion

    #region Validation

    private void Validate()
    {
      if (string.IsNullOrEmpty(_image))
        throw new FluentDockerException(
            "Container image is required. Call UseImage() before building.");

      if (_autoRemove && _keepContainer)
        throw new FluentDockerException(
            "WithAutoRemove() and KeepContainer() are mutually exclusive. " +
            "AutoRemove causes Docker to remove the container on exit.");

      if (_autoRemove && !string.IsNullOrEmpty(_restartPolicy) &&
          _restartPolicy != "no")
        throw new FluentDockerException(
            "WithAutoRemove() and a restart policy other than 'no' are mutually exclusive.");

      foreach (var port in _ports)
      {
        var containerPort = port.Key;
        var hostPort = port.Value;

        // Validate container port is numeric (with optional protocol suffix)
        var portPart = containerPort.Contains('/')
            ? containerPort.Substring(0, containerPort.IndexOf('/'))
            : containerPort;
        if (!int.TryParse(portPart, out var cp) || cp < 1 || cp > 65535)
          throw new FluentDockerException(
              $"Invalid container port '{containerPort}'. Port must be 1-65535.");

        // Validate host port if specified
        if (!string.IsNullOrEmpty(hostPort) &&
            int.TryParse(hostPort, out var hp) && (hp < 0 || hp > 65535))
          throw new FluentDockerException(
              $"Invalid host port '{hostPort}'. Port must be 0-65535 (0 for random).");
      }
    }

    #endregion

    #region Execute

    public async Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken)
    {
      Validate();
      var driver = _kernel.SysCtl<Drivers.IContainerDriver>(_driverId);
      var imageDriver = _kernel.SysCtl<Drivers.IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      // Handle existing container
      if (!string.IsNullOrEmpty(_name) && _existsBehavior != ContainerExistsBehavior.Default)
      {
        var existing = await FindExistingContainerAsync(driver, context, _name, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
          if (_existsBehavior == ContainerExistsBehavior.Reuse)
          {
            var reuseService = new Services.Impl.ContainerService(
                _kernel, _driverId, existing, _image, _name,
                !_keepRunning, !_keepContainer,
                _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                _customResolver, _lifecycleHooks);

            var inspectResult = await driver.InspectAsync(context, existing, cancellationToken).ConfigureAwait(false);
            if (inspectResult.Success && inspectResult.Data?.State?.Running != true)
              await reuseService.StartAsync(cancellationToken).ConfigureAwait(false);

            return reuseService;
          }
          else if (_existsBehavior == ContainerExistsBehavior.Destroy)
          {
            await driver.RemoveAsync(context, existing, _destroyForce, _destroyRemoveVolumes, cancellationToken).ConfigureAwait(false);
          }
        }
      }

      if (_forcePullImage && imageDriver != null)
        await imageDriver.PullAsync(context, _image, "latest", null, cancellationToken).ConfigureAwait(false);

      var config = new Drivers.ContainerCreateConfig
      {
        Image = _image,
        Name = _name,
        Environment = _environment,
        PortBindings = _ports,
        Command = _command.Count > 0 ? _command.ToArray() : null,
        Labels = _labels.Count > 0 ? _labels : null,
        Volumes = _volumes.Count > 0 ? _volumes : null,
        Networks = _networks.Count > 0 ? _networks : null,
        WorkingDirectory = _workingDir,
        User = _user,
        RestartPolicy = _restartPolicy,
        Hostname = _hostname,
        NetworkMode = _networkMode,
        Ipv4Address = _ipv4Address,
        Ipv6Address = _ipv6Address,
        MemoryLimit = _memoryLimit,
        CpuShares = _cpuShares,
        Privileged = _privileged,
        AutoRemove = _autoRemove,
        Links = _links.Count > 0
              ? _links.Select(l => l.Alias != l.ContainerName
                  ? $"{l.ContainerName}:{l.Alias}" : l.ContainerName).ToList()
              : null,
        NetworkAliases = _networkAliases.Count > 0
              ? _networkAliases
                  .GroupBy(a => a.NetworkName)
                  .ToDictionary(g => g.Key, g => g.Select(a => a.Alias).ToList())
              : null,
        Pod = _pod,
        CapAdd = _capAdd.Count > 0 ? _capAdd : null,
        CapDrop = _capDrop.Count > 0 ? _capDrop : null,
        SecurityOpt = _securityOpt.Count > 0 ? _securityOpt : null,
        ShmSize = _shmSize,
        Tmpfs = _tmpfs.Count > 0 ? _tmpfs : null,
        Devices = _devices.Count > 0 ? _devices : null,
        ReadonlyRootfs = _readonlyRootfs,
        Platform = _platform,
        Runtime = _runtime
      };

      var response = await driver.CreateAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
        throw new DriverException($"Failed to create container: {response.Error}",
            response.ErrorCode, response.ErrorContext);

      var service = new Services.Impl.ContainerService(
          _kernel, _driverId, response.Data.Id, _image, _name,
          !_keepRunning, !_keepContainer,
          _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
          _customResolver, _lifecycleHooks);

      _pendingService = service;
      var hasLinks = _links.Count > 0;

      if (!hasLinks)
      {
        try
        {
          await service.StartAsync(cancellationToken).ConfigureAwait(false);
          await WaitForContainerRunningAsync(driver, context, response.Data.Id, cancellationToken).ConfigureAwait(false);
          await ExecuteLifecycleHooksAsync(service, ServiceRunningState.Running, cancellationToken).ConfigureAwait(false);
          await ExecuteWaitConditionsAsync(service, cancellationToken).ConfigureAwait(false);
          _waitConditionsExecuted = true;
        }
        catch (Exception ex)
        {
          Logger.Log($"Container build failed: {ex.Message}");
          // Container was created (and possibly started). Force-remove to prevent leaks.
          // Use a bounded timeout so cleanup cannot hang indefinitely when the daemon is unhealthy.
          try
          {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await driver.RemoveAsync(context, response.Data.Id, true, false, cleanupCts.Token).ConfigureAwait(false);
          }
          catch { /* best effort cleanup */ }
          throw;
        }
      }

      return service;
    }

    #endregion
  }
}
