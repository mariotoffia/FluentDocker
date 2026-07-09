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
    private string _name;
    private string _driver = "bridge";
    private string _subnet;
    private string _gateway;
    private string _ipRange;
    private bool _enableIPv6;
    private bool _internal;
    private bool _removeOnDispose;
    private readonly Dictionary<string, string> _labels = [];
    private readonly Dictionary<string, string> _options = [];
    private string _createdNetworkId;

    internal bool CreatedResource { get; private set; }
    internal string Name => _name;

    public INetworkBuilder WithName(string name) { _name = name; return this; }
    public INetworkBuilder UseDriver(string driver) { _driver = driver; return this; }
    public INetworkBuilder WithSubnet(string subnet) { if (!System.Net.IPNetwork.TryParse(subnet, out _)) throw new FluentDockerException($"Invalid subnet '{subnet}'. Expected CIDR notation."); _subnet = subnet; return this; }
    public INetworkBuilder WithGateway(string gateway) { _gateway = gateway; return this; }
    public INetworkBuilder WithIPRange(string ipRange) { _ipRange = ipRange; return this; }
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
      if (_gateway != null && !System.Net.IPAddress.TryParse(_gateway, out _))
        throw new FluentDockerException($"Invalid gateway '{_gateway}'. Expected an IP address.");
      if (_ipRange != null && !System.Net.IPNetwork.TryParse(_ipRange, out _))
        throw new FluentDockerException($"Invalid IP range '{_ipRange}'. Expected CIDR notation.");

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
              _kernel, _driverId, existingNetwork.Id, _name, removeOnDispose: reownPriorAttempt && _removeOnDispose);
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
            response.ErrorCode, response.ErrorContext);

      CreatedResource = true;
      _createdNetworkId = response.Data.Id;
      return new Services.Impl.NetworkService(
          _kernel, _driverId, response.Data.Id, _name, _removeOnDispose);
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
    private string _name;
    private string _driver = "local";
    private bool _removeOnDispose;
    private readonly Dictionary<string, string> _driverOpts = [];
    private readonly Dictionary<string, string> _labels = [];

    internal bool CreatedResource { get; private set; }
    internal string Name => _name;

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
          return new Services.Impl.VolumeService(
              _kernel, _driverId, existing.Data.Name, existing.Data.Driver ?? _driver, removeOnDispose: priorAttemptCreated && _removeOnDispose);
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
            response.ErrorCode, response.ErrorContext);

      CreatedResource = true;
      return new Services.Impl.VolumeService(
          _kernel, _driverId, response.Data.Name, _driver, _removeOnDispose);
    }
  }

  /// <summary>
  /// Compose builder implementation.
  /// </summary>
  internal sealed partial class ComposeBuilder(FluentDockerKernel kernel, string driverId) : IComposeBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel = kernel;
    private readonly string _driverId = driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
#pragma warning disable IDE1006 // Same-assembly compose overlay needs the existing backing fields.
    internal readonly List<string> _composeFiles = [];
    internal readonly List<string> _profiles = [];
    internal string _projectName;
    internal readonly Dictionary<string, string> _environment = [];
#pragma warning restore IDE1006
    private readonly HashSet<string> _explicitEnvironmentKeys = [];
    private readonly List<string> _envFiles = [];
    private readonly Dictionary<string, int> _scale = [];
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
#pragma warning disable IDE1006 // Same-assembly compose overlay needs the existing backing field.
    internal bool _attachToExisting;
#pragma warning restore IDE1006
    private readonly List<string> _services = [];
    private ComposeModelBuilder _models;
    private string _renderedOverlay;

    public IComposeBuilder WithComposeFile(string path) { _composeFiles.Add(path); return this; }
    public IComposeBuilder WithComposeFiles(params string[] paths) { _composeFiles.AddRange(paths); return this; }

    internal IComposeBuilder WithModelsInternal(Action<IComposeModelBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(configure);
      _models ??= new ComposeModelBuilder();
      configure(_models);
      return this;
    }
    public IComposeBuilder WithProjectName(string name) { _projectName = name; return this; }
    public IComposeBuilder WithEnvironment(string key, string value)
    {
      _environment[key] = value;
      _explicitEnvironmentKeys.Add(key);
      return this;
    }

    public IComposeBuilder WithEnvironment(IDictionary<string, string> environment)
    {
      foreach (var kvp in environment)
      {
        _environment[kvp.Key] = kvp.Value;
        _explicitEnvironmentKeys.Add(kvp.Key);
      }
      return this;
    }

    public IComposeBuilder WithEnvFile(string path)
    {
      _envFiles.Add(path);
      return this;
    }

    public IComposeBuilder WithBuild(bool build = true) { _build = build; return this; }
    public IComposeBuilder WithForceRecreate(bool forceRecreate = true) { _forceRecreate = forceRecreate; return this; }
    public IComposeBuilder WithRemoveOrphans(bool removeOrphans = true) { _removeOrphans = removeOrphans; return this; }
    public IComposeBuilder WithRemoveVolumes(bool removeVolumes = true) { _removeVolumes = removeVolumes; return this; }
    public IComposeBuilder WithRemoveImages(bool removeImages = true) { _removeImages = removeImages; return this; }
    public IComposeBuilder ForServices(params string[] services) { _services.AddRange(services); return this; }
    public IComposeBuilder WithTimeout(int seconds) { if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Value must be non-negative."); _timeout = seconds; return this; }
    public IComposeBuilder WithScale(string service, int replicas) { if (replicas < 0) throw new ArgumentOutOfRangeException(nameof(replicas), replicas, "Value must be non-negative."); _scale[service] = replicas; return this; }
    public IComposeBuilder WithNoDeps(bool noDeps = true) { _noDeps = noDeps; return this; }
    public IComposeBuilder WithNoStart(bool noStart = true) { _noStart = noStart; return this; }
    public IComposeBuilder WithPull(bool always = true) { _pull = always; return this; }
    public IComposeBuilder WithWait(bool wait = true) { _wait = wait; return this; }

    public IComposeBuilder WithWaitTimeout(int seconds)
    {
      if (seconds < 0)
        throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Value must be non-negative.");
      _waitTimeout = seconds;
      _wait = true;
      return this;
    }
    public IComposeBuilder WithProfiles(params string[] profiles) { _profiles.AddRange(profiles); return this; }
    public IComposeBuilder ConnectToExisting(bool connect = true) { _attachToExisting = connect; return this; }
    internal bool BorrowedProject { get; private set; }

    public Task<IServiceAsync> ExecuteAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(TimeSpan.FromSeconds(30), cancellationToken);

    public async Task<IServiceAsync> ExecuteAsync(
        TimeSpan cleanupTimeout, CancellationToken cancellationToken)
    {
      var driver = _kernel.SysCtl<Drivers.IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);
      Validate();
      await LoadEnvFilesAsync(cancellationToken).ConfigureAwait(false);

      if (_attachToExisting && string.IsNullOrEmpty(_projectName) && _composeFiles.Count == 0)
        throw new FluentDockerException(
            "ConnectToExisting requires WithProjectName and/or WithComposeFile to identify the project.");

      // Render a first-class models: overlay (WithModels) to a managed temp file and
      // append it so Compose merges it. The ComposeService owns the file and deletes it
      // on teardown / dispose.
      var ownedTempFiles = RenderModelOverlay();

      // Attach to an already-running project: do NOT run `compose up`, just hand back a
      // service bound to the existing project (issue #305).
      if (_attachToExisting)
      {
        BorrowedProject = true;
        return new Services.Impl.ComposeService(
            _kernel, _driverId, [.. _composeFiles], _projectName, _removeVolumes, _removeImages, ownedTempFiles,
            downOnDispose: false,
            initialState: ServiceRunningState.Unknown);
      }

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

      var borrowedProject = await ComposeProjectExistsAsync(driver, context, config, cancellationToken)
          .ConfigureAwait(false);
      BorrowedProject = borrowedProject;
      CommandResponse<Drivers.ComposeUpResult> response;
      try
      {
        response = await driver.UpAsync(context, config, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        await CleanupFailedComposeAsync(driver, context, config, _removeVolumes, borrowedProject, cleanupTimeout)
            .ConfigureAwait(false);
        RemoveComposeFiles(ownedTempFiles);
        DeleteTempFiles(ownedTempFiles);
        throw;
      }

      if (!response.Success)
      {
        await CleanupFailedComposeAsync(driver, context, config, _removeVolumes, borrowedProject, cleanupTimeout)
            .ConfigureAwait(false);
        // Up failed: no ComposeService is created to own the overlay, so clean it up here.
        RemoveComposeFiles(ownedTempFiles);
        DeleteTempFiles(ownedTempFiles);
        throw new DriverException($"Failed to start compose: {response.Error}",
            response.ErrorCode, response.ErrorContext);
      }

      return new Services.Impl.ComposeService(
          _kernel, _driverId, [.. _composeFiles],
          response.Data.ProjectName ?? _projectName,
          _removeVolumes, _removeImages, ownedTempFiles,
          downOnDispose: !borrowedProject,
          initialState: _noStart ? ServiceRunningState.Stopped : ServiceRunningState.Running);
    }

    internal async Task LoadEnvFilesAsync(CancellationToken cancellationToken)
    {
      foreach (var path in _envFiles)
      {
        if (!System.IO.File.Exists(path))
          throw new System.IO.FileNotFoundException(
              $"Compose env file was not found: {path}", path);

        foreach (var line in await System.IO.File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
          var trimmed = line.Trim();
          if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            continue;
          if (trimmed.StartsWith("export ", StringComparison.Ordinal))
            trimmed = trimmed["export ".Length..].TrimStart();
          var eqIndex = trimmed.IndexOf('=');
          if (eqIndex <= 0)
            continue;
          var key = trimmed[..eqIndex].Trim();
          if (string.IsNullOrEmpty(key))
            continue;
          if (_explicitEnvironmentKeys.Contains(key) || Environment.GetEnvironmentVariable(key) != null)
            continue;
          // ponytail: trim outer whitespace off the unquoted value (compose-go/godotenv parity);
          // StripEnvValueQuotes then removes surrounding quotes, preserving quoted inner spaces.
          _environment[key] = StripEnvValueQuotes(
              StripUnquotedInlineComment(trimmed[(eqIndex + 1)..]).Trim());
        }
      }
    }

    /// <summary>
    /// Renders the configured <see cref="ComposeModelBuilder"/> (if any) to a unique temp
    /// overlay file, appends it to the compose-files list and returns the owned temp-file
    /// list (or null when no models were configured).
    /// </summary>
    private IReadOnlyList<string> RenderModelOverlay()
    {
      if (_models is null)
        return null;

      // Drop the overlay from a previous (failed) attempt so retries do not
      // accumulate stale, possibly deleted, temp-file paths.
      if (_renderedOverlay is not null)
        _composeFiles.Remove(_renderedOverlay);

      var path = Path.Combine(
          Path.GetTempPath(),
          $"fluentdocker-models-{Guid.NewGuid():N}.yml");
      try
      {
        _models.WriteOverlay(path);
      }
      catch
      {
        // The caller never receives this path on throw, so it could never be cleaned up.
        // Delete the partially written overlay before propagating.
        try
        {
          if (File.Exists(path))
            File.Delete(path);
        }
        catch { /* best effort */ }
        throw;
      }
      _composeFiles.Add(path);
      _renderedOverlay = path;
      return [path];
    }

    private static void DeleteTempFiles(IReadOnlyList<string> files)
    {
      if (files is null)
        return;

      foreach (var f in files)
      {
        try
        {
          if (!string.IsNullOrEmpty(f) && File.Exists(f))
            File.Delete(f);
        }
        catch
        {
          // Best-effort cleanup on the build-failure path.
        }
      }
    }

    private void RemoveComposeFiles(IReadOnlyList<string> files)
    {
      if (files is null)
        return;

      foreach (var f in files)
        _composeFiles.Remove(f);
    }
  }
}
