using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders.Compose;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Network builder implementation.
  /// </summary>
  internal sealed class NetworkBuilder(FluentDockerKernel kernel, string driverId) : INetworkBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly string _driverId = driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private string? _name;
    private string _driver = "bridge";
    private string? _subnet;
    private string? _gateway;
    private string? _ipRange;
    private bool _enableIPv6;
    private bool _internal;
    private bool _removeOnDispose;
    private readonly Dictionary<string, string> _labels = [];
    private readonly Dictionary<string, string> _options = [];
    private string? _createdNetworkId;

    internal bool CreatedResource { get; private set; }
    internal string Name => _name!;

    public INetworkBuilder WithName(string name) { _name = name; return this; }
    public INetworkBuilder UseDriver(string driver) { _driver = driver; return this; }
    public INetworkBuilder WithSubnet(string subnet) { if (!System.Net.IPNetwork.TryParse(subnet, out _)) throw new FluentDockerException($"Invalid subnet '{subnet}'. Expected CIDR notation."); _subnet = subnet; return this; }
    public INetworkBuilder WithGateway(string gateway) { if (!System.Net.IPAddress.TryParse(gateway, out _)) throw new FluentDockerException($"Invalid gateway '{gateway}'. Expected an IP address."); _gateway = gateway; return this; }
    public INetworkBuilder WithIPRange(string ipRange) { if (!System.Net.IPNetwork.TryParse(ipRange, out _)) throw new FluentDockerException($"Invalid IP range '{ipRange}'. Expected CIDR notation."); _ipRange = ipRange; return this; }
    public INetworkBuilder WithIPv6(bool enableIPv6 = true) { _enableIPv6 = enableIPv6; return this; }
    public INetworkBuilder AsInternal(bool isInternal = true) { _internal = isInternal; return this; }
    public INetworkBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }
    public INetworkBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
    public INetworkBuilder WithOption(string key, string value) { _options[key] = value; return this; }

    public async Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken)
    {
      var priorAttemptCreated = CreatedResource;
      CreatedResource = false;
      if (string.IsNullOrWhiteSpace(_name))
        throw new FluentDockerException("Network name is required. Call WithName() before building.");
      // Gateway/IP-range/subnet are validated eagerly in their With* setters (same timing as
      // WithSubnet), so the fields can only hold parseable values here.

      var driver = _kernel.SysCtl<Drivers.INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var listResult = await driver.ListAsync(context, null, cancellationToken).ConfigureAwait(false);
      if (listResult.Success)
      {
        var existingNetwork = listResult.Data?.FirstOrDefault(n =>
            string.Equals(n.Name, _name, StringComparison.Ordinal));

        if (existingNetwork != null)
        {
          // Building must never delete a pre-existing resource the builder did not create.
          // Reuse the existing network as a borrowed (non-removing) wrapper; _removeOnDispose
          // only governs networks this builder actually creates below.
          if (priorAttemptCreated || _removeOnDispose || _subnet != null || _gateway != null || _ipRange != null || _enableIPv6 || _internal
              || _labels.Count > 0 || _options.Count > 0
              || !string.Equals(_driver, "bridge", StringComparison.OrdinalIgnoreCase))
          {
            _kernel.LoggerFactory.CreateLogger<NetworkBuilder>().LogWarning(
                "Network '{Name}' already exists; reusing it. Requested configuration " +
                "(subnet/gateway/ip-range/driver/labels/options/internal/ipv6/RemoveOnDispose) may be ignored. " +
                "If this was left over from a prior failed build attempt, state may be dirty.",
                _name);
          }

          var reownPriorAttempt = priorAttemptCreated &&
              string.Equals(existingNetwork.Id, _createdNetworkId, StringComparison.Ordinal);
          CreatedResource = reownPriorAttempt;
          // Re-own only by Docker's network ID. Names are ambiguous; IDs prove this is the
          // same network this builder created before cleanup missed it.
          return new Services.Impl.NetworkService(
              _kernel, _driverId, existingNetwork.Id!, _name, removeOnDispose: reownPriorAttempt && _removeOnDispose);
        }
      }

      var config = new Drivers.NetworkCreateConfig
      {
        Name = _name,
        Driver = _driver,
        Subnet = _subnet,
        Gateway = _gateway,
        IpRange = _ipRange,
        EnableIPv6 = _enableIPv6,
        Internal = _internal,
        Labels = _labels,
        Options = _options
      };

      var response = await driver.CreateAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
        throw new DriverException($"Failed to create network: {response.Error}",
            response.ErrorCode!, response.ErrorContext);

      CreatedResource = true;
      _createdNetworkId = response.Data!.Id;
      return new Services.Impl.NetworkService(
          _kernel, _driverId, response.Data.Id!, _name, _removeOnDispose);
    }
  }

  /// <summary>
  /// Volume builder implementation.
  /// </summary>
  internal sealed class VolumeBuilder(FluentDockerKernel kernel, string driverId) : IVolumeBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly string _driverId = driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private string? _name;
    private string _driver = "local";
    private bool _removeOnDispose;
    private readonly Dictionary<string, string> _driverOpts = [];
    private readonly Dictionary<string, string> _labels = [];

    internal bool CreatedResource { get; private set; }
    internal string Name => _name!;

    public IVolumeBuilder WithName(string name) { _name = name; return this; }
    public IVolumeBuilder UseDriver(string driver) { _driver = driver; return this; }
    public IVolumeBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }
    public IVolumeBuilder WithDriverOption(string key, string value) { _driverOpts[key] = value; return this; }
    public IVolumeBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }

    public async Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken)
    {
      var priorAttemptCreated = CreatedResource;
      CreatedResource = false;
      var driver = _kernel.SysCtl<Drivers.IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      if (!string.IsNullOrEmpty(_name))
      {
        // `docker/podman volume create` is idempotent and would silently ADOPT a pre-existing
        // volume; a later RemoveOnDispose() would then delete a user's volume (with its data).
        // Building must never delete a resource it did not create, so reuse any existing volume
        // as a borrowed (non-removing) wrapper. _removeOnDispose only governs volumes created below.
        var existing = await driver.InspectAsync(context, _name, cancellationToken).ConfigureAwait(false);
        if (existing is { Success: true, Data: not null })
        {
          if (priorAttemptCreated || _removeOnDispose || _driverOpts.Count > 0 || _labels.Count > 0
              || !string.Equals(_driver, "local", StringComparison.OrdinalIgnoreCase))
          {
            _kernel.LoggerFactory.CreateLogger<VolumeBuilder>().LogWarning(
                "Volume '{Name}' already exists; reusing it. Requested configuration " +
                "(driver/options/labels/RemoveOnDispose) may be ignored. If this was left over " +
                "from a prior failed build attempt, state may be dirty.",
                _name);
          }

          // Re-own across retries: when THIS builder created the volume on a prior attempt and
          // RemoveOnDispose was requested, honor removal on the reused volume. A Docker volume
          // name is its identity (unlike a network name), so "the volume named X" is unambiguous
          // and re-owning by name is safe. Narrow exception: if a prior attempt's cleanup already
          // removed our volume and an external actor recreated the same name in between, we re-own
          // that name too — acceptable, since the caller explicitly asked to manage (and remove)
          // the volume named X. A volume not created on any attempt stays borrowed, never removed.
          // Restore ownership so a failed later attempt force-removes it and the failure manifest
          // reports it as builder-created, not "borrowed" (mirrors NetworkBuilder).
          CreatedResource = priorAttemptCreated;
          return new Services.Impl.VolumeService(
              _kernel, _driverId, existing.Data.Name!, existing.Data.Driver ?? _driver, removeOnDispose: priorAttemptCreated && _removeOnDispose);
        }
      }

      var config = new Drivers.VolumeCreateConfig
      {
        Name = _name,
        Driver = _driver,
        DriverOpts = _driverOpts,
        Labels = _labels
      };

      var response = await driver.CreateAsync(context, config, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
        throw new DriverException($"Failed to create volume: {response.Error}",
            response.ErrorCode!, response.ErrorContext);

      CreatedResource = true;
      return new Services.Impl.VolumeService(
          _kernel, _driverId, response.Data!.Name!, _driver, _removeOnDispose);
    }
  }

}
