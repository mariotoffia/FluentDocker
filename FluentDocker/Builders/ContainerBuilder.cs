using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Container builder implementation.
  /// </summary>
  internal sealed partial class ContainerBuilder(FluentDockerKernel kernel, string driverId) : IContainerBuilder, IDriverScopedBuilder
  {
    private static readonly char[] EqualsSeparator = ['='];
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly ILogger<ContainerBuilder> _logger = kernel.LoggerFactory.CreateLogger<ContainerBuilder>();
    private readonly string _driverId = driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private string _image;
    private string _name;
    private readonly Dictionary<string, string> _environment = [];
    private readonly Dictionary<string, string> _extraHosts = [];
    private readonly Dictionary<string, string> _ports = [];
    private readonly HashSet<string> _duplicateContainerPorts = [];
    private readonly List<string> _command = [];
    private readonly List<string> _volumes = [];
    private readonly Dictionary<string, string> _labels = [];
    private readonly List<string> _networks = [];
    private readonly List<NetworkAlias> _networkAliases = [];
    private readonly List<WaitCondition> _waitConditions = [];
    private readonly List<LifecycleHook> _lifecycleHooks = [];
    private readonly List<ContainerLink> _links = [];

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
    private readonly List<string> _capAdd = [];
    private readonly List<string> _capDrop = [];
    private readonly List<string> _securityOpt = [];
    private long? _shmSize;
    private readonly Dictionary<string, string> _tmpfs = [];
    private readonly Dictionary<string, string> _devices = [];
    private bool _readonlyRootfs;
    private string _platform;
    private string _runtime;
    private bool _interactive;
    private bool _tty;
    private string[] _entrypoint;
    private string _stopSignal;
    private Drivers.HealthCheckConfig _healthCheck;
    private readonly List<string> _dns = [];
    private int _waitPollIntervalMs = 500;
    private Services.Impl.ContainerService _pendingService;
    private bool _waitConditionsExecuted;
    private bool _reusedExisting;
    private bool _startDeferred;

    internal bool AllowCleanExitOnStart => _waitConditions.Count == 0;
    internal long StartupTimeoutMs => _startupTimeoutMs ?? (_waitConditions.Count == 0 ? 3000 : Math.Max(3000, _waitConditions.Max(c => c.TimeoutMs)));
    internal int StartupPollIntervalMs => _waitConditions.Count == 0 ? 100 : Math.Clamp(_waitPollIntervalMs, 1, 100);

    #region Basic Configuration

    public IContainerBuilder UseImage(string image) { _image = image; return this; }
    public IContainerBuilder WithName(string name) { _name = name; return this; }

    public IContainerBuilder WithEnvironment(string key, string value)
    {
      ValidateEnvironmentName(key, $"Expected format name=value, empty name in the name value string: '{key}={value}'");
      _environment[key] = value;
      return this;
    }

    public IContainerBuilder WithEnvironment(string keyValue)
    {
      ArgumentNullException.ThrowIfNull(keyValue);
      var parts = keyValue.Split(EqualsSeparator, 2);
      ValidateEnvironmentName(parts[0], $"Expected format name=value, empty name in the name value string: '{keyValue}'");
      if (parts.Length == 2)
        _environment[parts[0]] = parts[1];
      else
        _environment[keyValue] = string.Empty;
      return this;
    }

    public IContainerBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
    public IContainerBuilder WithWorkingDirectory(string workingDir) { _workingDir = workingDir; return this; }
    public IContainerBuilder WithUser(string user) { _user = user; return this; }
    public IContainerBuilder WithRestartPolicy(string policy) { _restartPolicy = policy; return this; }
    public IContainerBuilder WithHostname(string hostname) { _hostname = hostname; return this; }

    /// <inheritdoc />
    public IContainerBuilder WithExtraHost(string host, string ip) { _extraHosts[host] = ip; return this; }
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
    public IContainerBuilder WithMemoryLimit(long bytes) { ValidateNonNegative(bytes, nameof(bytes)); _memoryLimit = bytes; return this; }
    public IContainerBuilder WithCpuShares(long shares) { ValidateNonNegative(shares, nameof(shares)); _cpuShares = shares; return this; }
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
    public IContainerBuilder WithShmSize(long bytes) { ValidateNonNegative(bytes, nameof(bytes)); _shmSize = bytes; return this; }
    public IContainerBuilder WithTmpfs(string containerPath, string options = null) { _tmpfs[containerPath] = options ?? ""; return this; }
    public IContainerBuilder WithDevice(string hostDevice, string containerDevice = null) { _devices[hostDevice] = containerDevice ?? hostDevice; return this; }
    public IContainerBuilder WithReadonlyRootfs() { _readonlyRootfs = true; return this; }
    public IContainerBuilder WithPlatform(string platform) { _platform = platform; return this; }
    public IContainerBuilder WithRuntime(string runtime) { _runtime = runtime; return this; }
    public IContainerBuilder WithStopSignal(string signal) { _stopSignal = signal; return this; }

    public IContainerBuilder WithDns(params string[] servers)
    {
      _dns.AddRange(servers);
      return this;
    }

    public IContainerBuilder WithHealthCheck(
        string cmd, string interval = null, string timeout = null,
        int retries = 0, string startPeriod = null)
    {
      _healthCheck = new Drivers.HealthCheckConfig
      {
        Test = ["CMD-SHELL", cmd],
        Interval = interval,
        Timeout = timeout,
        Retries = retries,
        StartPeriod = startPeriod
      };
      return this;
    }

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

    #region Dispose Behavior

    public IContainerBuilder KeepContainer() { _keepContainer = true; return this; }
    public IContainerBuilder KeepRunning() { _keepRunning = true; _keepContainer = true; return this; }
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

        ValidateContainerPort(containerPort);

        ValidateHostPort(hostPort);
      }
      if (_duplicateContainerPorts.Count > 0)
        throw new FluentDockerException(
            $"Duplicate container port mapping for '{_duplicateContainerPorts.First()}'. Configure each container port only once.");
      ValidateHardenedConfiguration();
    }

    private static void ValidateContainerPort(string containerPort)
    {
      var slash = containerPort.IndexOf('/');
      var portPart = slash >= 0 ? containerPort[..slash] : containerPort;
      if (slash >= 0 && !IsKnownProtocol(containerPort[(slash + 1)..]))
        throw new FluentDockerException(
            $"Invalid container port '{containerPort}'. Protocol must be tcp, udp, or sctp.");
      if (!IsValidPortRange(portPart, allowZero: false))
        throw new FluentDockerException(
            $"Invalid container port '{containerPort}'. Port must be 1-65535.");
    }

    internal static void ValidateHostPort(string hostPort)
    {
      if (string.IsNullOrEmpty(hostPort))
        return;

      var portPart = hostPort;
      var colon = hostPort.LastIndexOf(':');
      if (colon >= 0)
      {
        var hostIp = hostPort[..colon];
        portPart = hostPort[(colon + 1)..];
        if (string.IsNullOrEmpty(hostIp) || !IPAddress.TryParse(hostIp, out _))
          throw new FluentDockerException(
              $"Invalid host port '{hostPort}'. Host binding must be [ip:]port[-range].");
      }

      if (!IsValidPortRange(portPart, allowZero: true))
        throw new FluentDockerException(
            $"Invalid host port '{hostPort}'. Port must be 0-65535 (0 for random).");
    }

    private static bool IsKnownProtocol(string protocol) =>
        string.Equals(protocol, "tcp", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(protocol, "udp", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(protocol, "sctp", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidPortRange(string value, bool allowZero)
    {
      var parts = value.Split('-', 2);
      if (!IsValidPort(parts[0], allowZero, out var start))
        return false;
      if (parts.Length == 1)
        return true;
      return IsValidPort(parts[1], allowZero, out var end) && start <= end;
    }

    private static bool IsValidPort(string value, bool allowZero, out int port) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
        port <= 65535 &&
        (allowZero ? port >= 0 : port >= 1);

    private static string NormalizeContainerPort(string containerPort)
    {
      var slash = containerPort.IndexOf('/');
      return slash < 0 ? $"{containerPort}/tcp" : $"{containerPort[..slash]}/{containerPort[(slash + 1)..].ToLowerInvariant()}";
    }

    #endregion

    #region Execute

    public Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(TimeSpan.FromSeconds(120), cancellationToken);

    internal async Task<IServiceAsync> ExecuteAsync(
        TimeSpan cleanupTimeout, CancellationToken cancellationToken)
    {
      Validate();
      var driver = _kernel.SysCtl<Drivers.IContainerDriver>(_driverId);
      _kernel.TrySysCtl<Drivers.IImageDriver>(_driverId, out var imageDriver);
      var context = new DriverContext(_driverId);

      if (!string.IsNullOrEmpty(_name) && _existsBehavior != ContainerExistsBehavior.Default)
      {
        var existing = await FindExistingContainerAsync(driver, context, _name, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
          if (_existsBehavior == ContainerExistsBehavior.Reuse)
          {
            _reusedExisting = true;
            var reuseService = new Services.Impl.ContainerService(
                _kernel, _driverId, existing, _image, _name,
                false, false,
                _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                _customResolver, _lifecycleHooks);

            var inspectResult = await driver.InspectAsync(context, existing, cancellationToken).ConfigureAwait(false);
            if (inspectResult.Success && inspectResult.Data?.State?.Running != true)
            {
              await StartReusedContainerAsync(driver, context, existing, reuseService, cancellationToken).ConfigureAwait(false);
              _pendingService = reuseService;
              _waitConditionsExecuted = true;
              await RunPostStartAsync(reuseService, cancellationToken).ConfigureAwait(false);
            }
            else
            {
              await reuseService.InspectAsync(cancellationToken).ConfigureAwait(false);
              _pendingService = reuseService;
              _waitConditionsExecuted = true;
              // Borrowed running container: verify readiness (wait conditions) but do NOT re-run
              // start hooks (CopyToOnStart/ExecuteOnRunning). Those fire only when THIS build starts
              // the container; re-running them on an already-running container would repeat side
              // effects (e.g. seed/migration commands).
              await ExecuteWaitConditionsAsync(reuseService, cancellationToken).ConfigureAwait(false);
              if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    "Reusing running container '{Name}'; wait conditions verified. Start hooks " +
                    "(CopyToOnStart/ExecuteOnRunning) and configuration differences are not applied.",
                    _name);
            }

            return reuseService;
          }
          else if (_existsBehavior == ContainerExistsBehavior.Destroy)
          {
            var remove = await driver.RemoveAsync(context, existing,
                _destroyForce, _destroyRemoveVolumes, cancellationToken).ConfigureAwait(false);
            if (!remove.Success)
              throw new DriverException($"Failed to remove existing container '{_name}': {remove.Error}",
                  remove.ErrorCode, remove.ErrorContext);
          }
        }
      }
      if (_forcePullImage && imageDriver == null)
        throw new FluentDockerException("ForcePullImage() requires a driver that supports IImageDriver.");
      if (_forcePullImage)
        await ExecuteForcePullAsync(imageDriver, context, cancellationToken).ConfigureAwait(false);

      var config = new Drivers.ContainerCreateConfig
      {
        Image = _image,
        Name = _name,
        Environment = _environment,
        PortBindings = _ports,
        Command = _command.Count > 0 ? [.. _command] : null,
        Labels = _labels.Count > 0 ? _labels : null,
        Volumes = _volumes.Count > 0 ? _volumes : null,
        Networks = _networks.Count > 0 ? _networks : null,
        WorkingDirectory = _workingDir,
        User = _user,
        RestartPolicy = _restartPolicy,
        Hostname = _hostname,
        ExtraHosts = _extraHosts,
        NetworkMode = _networkMode,
        Ipv4Address = _ipv4Address,
        Ipv6Address = _ipv6Address,
        MemoryLimit = _memoryLimit,
        CpuShares = _cpuShares,
        Privileged = _privileged,
        AutoRemove = _autoRemove,
        Links = _links.Count > 0
              ? [.. _links.Select(l => l.Alias != l.ContainerName
                  ? $"{l.ContainerName}:{l.Alias}" : l.ContainerName)]
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
        Runtime = _runtime,
        Interactive = _interactive,
        Tty = _tty,
        Entrypoint = _entrypoint?.Length > 0 ? _entrypoint : null,
        StopSignal = _stopSignal,
        HealthCheck = _healthCheck,
        Dns = _dns.Count > 0 ? _dns : null
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
      // Capture deferred-start intent at EXECUTE time. Reuse branches return before this point,
      // so a reused (already-existing) container is never re-started by the deferred link pass.
      _startDeferred = hasLinks;

      if (!hasLinks)
      {
        try
        {
          await service.StartAsync(cancellationToken).ConfigureAwait(false);
          await WaitForContainerStartedAsync(
              driver, context, response.Data.Id, _name, AllowCleanExitOnStart,
              StartupTimeoutMs, StartupPollIntervalMs, cancellationToken).ConfigureAwait(false);
          _waitConditionsExecuted = true;
          await RunPostStartAsync(service, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Container build failed");
          _waitConditionsExecuted = false;

          var logTail = ex is OperationCanceledException
              ? null
              : await ReadLogTailAsync(driver, context, response.Data.Id, cancellationToken).ConfigureAwait(false);
          try
          {
            if (!_keepContainer)
            {
              using var cleanupCts = new CancellationTokenSource(cleanupTimeout);
              await service.RemoveAsync(force: true, removeVolumes: true, cleanupCts.Token).ConfigureAwait(false);
            }
          }
          catch { /* best effort cleanup */ }
          if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            throw;
          if (!string.IsNullOrWhiteSpace(logTail))
            ex.Data["ContainerLogTail"] = logTail;
          if (ex.GetType() == typeof(FluentDockerException))
            throw new FluentDockerException(AppendLogTail(ex.Message, logTail), ex);
          throw;
        }
      }

      return service;
    }

    #endregion
  }
}
