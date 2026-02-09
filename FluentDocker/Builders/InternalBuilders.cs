using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Network builder implementation.
  /// </summary>
  internal class NetworkBuilder : INetworkBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private string _name;
    private string _driver = "bridge";
    private string _subnet;
    private string _gateway;
    private string _ipRange;
    private bool _enableIPv6;
    private bool _internal;
    private bool _removeOnDispose;
    private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _options = new Dictionary<string, string>();

    public NetworkBuilder(FluentDockerKernel kernel, string driverId)
    {
      _kernel = kernel;
      _driverId = driverId;
    }

    public INetworkBuilder WithName(string name) { _name = name; return this; }
    public INetworkBuilder UseDriver(string driver) { _driver = driver; return this; }
    public INetworkBuilder WithSubnet(string subnet) { _subnet = subnet; return this; }
    public INetworkBuilder WithGateway(string gateway) { _gateway = gateway; return this; }
    public INetworkBuilder WithIPRange(string ipRange) { _ipRange = ipRange; return this; }
    public INetworkBuilder WithIPv6(bool enableIPv6 = true) { _enableIPv6 = enableIPv6; return this; }
    public INetworkBuilder AsInternal(bool isInternal = true) { _internal = isInternal; return this; }
    public INetworkBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }
    public INetworkBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
    public INetworkBuilder WithOption(string key, string value) { _options[key] = value; return this; }

    public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
    {
      var driver = _kernel.SysCtl<Drivers.INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var listResult = await driver.ListAsync(context, null, cancellationToken);
      if (listResult.Success)
      {
        var existingNetwork = listResult.Data?.FirstOrDefault(n =>
            string.Equals(n.Name, _name, StringComparison.OrdinalIgnoreCase));

        if (existingNetwork != null)
        {
          if (_removeOnDispose)
          {
            await driver.RemoveAsync(context, existingNetwork.Id, cancellationToken);
          }
          else
          {
            return new Services.Impl.NetworkService(
                _kernel, _driverId, existingNetwork.Id, _name, _removeOnDispose);
          }
        }
      }

      var config = new Drivers.NetworkCreateConfig
      {
        Name = _name,
        Driver = _driver,
        Subnet = _subnet,
        Gateway = _gateway,
        EnableIPv6 = _enableIPv6,
        Internal = _internal,
        Labels = _labels,
        Options = _options
      };

      if (!string.IsNullOrEmpty(_ipRange))
        config.Options["com.docker.network.bridge.ip-range"] = _ipRange;

      var response = await driver.CreateAsync(context, config, cancellationToken);
      if (!response.Success)
        throw new DriverException($"Failed to create network: {response.Error}",
            response.ErrorCode, response.ErrorContext);

      return new Services.Impl.NetworkService(
          _kernel, _driverId, response.Data.Id, _name, _removeOnDispose);
    }
  }

  /// <summary>
  /// Volume builder implementation.
  /// </summary>
  internal class VolumeBuilder : IVolumeBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private string _name;
    private string _driver = "local";
    private bool _removeOnDispose;
    private readonly Dictionary<string, string> _driverOpts = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();

    public VolumeBuilder(FluentDockerKernel kernel, string driverId)
    {
      _kernel = kernel;
      _driverId = driverId;
    }

    public IVolumeBuilder WithName(string name) { _name = name; return this; }
    public IVolumeBuilder UseDriver(string driver) { _driver = driver; return this; }
    public IVolumeBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }
    public IVolumeBuilder WithDriverOption(string key, string value) { _driverOpts[key] = value; return this; }
    public IVolumeBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }

    public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
    {
      var driver = _kernel.SysCtl<Drivers.IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new Drivers.VolumeCreateConfig
      {
        Name = _name,
        Driver = _driver,
        DriverOpts = _driverOpts,
        Labels = _labels
      };

      var response = await driver.CreateAsync(context, config, cancellationToken);
      if (!response.Success)
        throw new DriverException($"Failed to create volume: {response.Error}",
            response.ErrorCode, response.ErrorContext);

      return new Services.Impl.VolumeService(
          _kernel, _driverId, response.Data.Name, _driver, _removeOnDispose);
    }
  }

  /// <summary>
  /// Compose builder implementation.
  /// </summary>
  internal class ComposeBuilder : IComposeBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private readonly List<string> _composeFiles = new List<string>();
    private readonly List<string> _envFiles = new List<string>();
    private readonly List<string> _profiles = new List<string>();
    private string _projectName;
    private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
    private readonly Dictionary<string, int> _scale = new Dictionary<string, int>();
    private bool _build;
    private bool _forceRecreate;
    private bool _removeOrphans;
    private bool _removeVolumes;
    private bool _removeImages;
    private bool _noDeps;
    private bool _noStart;
    private bool _pull;
    private bool _wait;
    private int? _timeout;
    private int? _waitTimeout;
    private readonly List<string> _services = new List<string>();

    public ComposeBuilder(FluentDockerKernel kernel, string driverId)
    {
      _kernel = kernel;
      _driverId = driverId;
    }

    public IComposeBuilder WithComposeFile(string path) { _composeFiles.Add(path); return this; }
    public IComposeBuilder WithComposeFiles(params string[] paths) { _composeFiles.AddRange(paths); return this; }
    public IComposeBuilder WithProjectName(string name) { _projectName = name; return this; }
    public IComposeBuilder WithEnvironment(string key, string value) { _environment[key] = value; return this; }

    public IComposeBuilder WithEnvironment(IDictionary<string, string> environment)
    {
      foreach (var kvp in environment)
        _environment[kvp.Key] = kvp.Value;
      return this;
    }

    public IComposeBuilder WithEnvFile(string path)
    {
      _envFiles.Add(path);
      if (System.IO.File.Exists(path))
      {
        foreach (var line in System.IO.File.ReadAllLines(path))
        {
          var trimmed = line.Trim();
          if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
            continue;
          var eqIndex = trimmed.IndexOf('=');
          if (eqIndex > 0)
          {
            var key = trimmed.Substring(0, eqIndex);
            var value = trimmed.Substring(eqIndex + 1);
            _environment[key] = value;
          }
        }
      }
      return this;
    }

    public IComposeBuilder WithBuild(bool build = true) { _build = build; return this; }
    public IComposeBuilder WithForceRecreate(bool forceRecreate = true) { _forceRecreate = forceRecreate; return this; }
    public IComposeBuilder WithRemoveOrphans(bool removeOrphans = true) { _removeOrphans = removeOrphans; return this; }
    public IComposeBuilder WithRemoveVolumes(bool removeVolumes = true) { _removeVolumes = removeVolumes; return this; }
    public IComposeBuilder WithRemoveImages(bool removeImages = true) { _removeImages = removeImages; return this; }
    public IComposeBuilder ForServices(params string[] services) { _services.AddRange(services); return this; }
    public IComposeBuilder WithTimeout(int seconds) { _timeout = seconds; return this; }
    public IComposeBuilder WithScale(string service, int replicas) { _scale[service] = replicas; return this; }
    public IComposeBuilder WithNoDeps(bool noDeps = true) { _noDeps = noDeps; return this; }
    public IComposeBuilder WithNoStart(bool noStart = true) { _noStart = noStart; return this; }
    public IComposeBuilder WithPull(bool always = true) { _pull = always; return this; }
    public IComposeBuilder WithWait(bool wait = true) { _wait = wait; return this; }

    public IComposeBuilder WithWaitTimeout(int seconds)
    {
      _waitTimeout = seconds;
      _wait = true;
      return this;
    }

    public IComposeBuilder WithProfiles(params string[] profiles) { _profiles.AddRange(profiles); return this; }

    public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
    {
      var driver = _kernel.SysCtl<Drivers.IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new Drivers.ComposeUpConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Environment = _environment,
        Build = _build,
        ForceRecreate = _forceRecreate,
        RemoveOrphans = _removeOrphans,
        Services = _services,
        Detached = true,
        NoDeps = _noDeps,
        NoStart = _noStart,
        Wait = _wait,
        WaitTimeout = _waitTimeout,
        Timeout = _timeout,
        Pull = _pull ? "always" : null,
        Scale = _scale,
        Profiles = _profiles
      };

      var response = await driver.UpAsync(context, config, cancellationToken);
      if (!response.Success)
        throw new DriverException($"Failed to start compose: {response.Error}",
            response.ErrorCode, response.ErrorContext);

      return new Services.Impl.ComposeService(
          _kernel, _driverId, _composeFiles,
          response.Data.ProjectName ?? _projectName,
          _removeVolumes, _removeImages);
    }
  }
}
