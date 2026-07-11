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

    public IComposeBuilder WithComposeFile(string path) { ArgumentException.ThrowIfNullOrWhiteSpace(path); _composeFiles.Add(path); return this; }
    public IComposeBuilder WithComposeFiles(params string[] paths) { ArgumentNullException.ThrowIfNull(paths); foreach (var path in paths) WithComposeFile(path); return this; }

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
      // service bound to the existing project (issue #305). Probe first so a typo'd project
      // name fails at the attach call site instead of far downstream. The probe is fail-safe
      // (returns true on any list error), so we only throw when the project is *provably* absent.
      if (_attachToExisting)
      {
        var probeConfig = new Drivers.ComposeUpConfig
        {
          ComposeFiles = _composeFiles,
          ProjectName = _projectName,
          Environment = _environment
        };
        if (!await ComposeProjectExistsAsync(driver, context, probeConfig, cancellationToken).ConfigureAwait(false))
          throw new FluentDockerException(
              $"ConnectToExisting could not find a running compose project " +
              $"'{_projectName ?? "<derived>"}'. Verify the project name / compose file, or start it first.");

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
      CommandResponse<Drivers.ComposeUpResult> response;
      var borrowedProject = true;
      try
      {
        borrowedProject = await ComposeProjectExistsAsync(driver, context, config, cancellationToken)
            .ConfigureAwait(false);
        if (borrowedProject && _priorAttemptCreatedProject)
          // Re-own our own leftover from a failed prior attempt (its down-on-cleanup failed)
          // instead of treating it as a genuine external borrow -- otherwise it survives dispose
          // forever. Not a real borrow, so no implicit-borrow warning below.
          borrowedProject = false;
        BorrowedProject = borrowedProject;
        if (borrowedProject)
          LogImplicitBorrow(config);
        else
          _priorAttemptCreatedProject = true;
        response = await driver.UpAsync(context, config, cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        var cleanedUp = await CleanupFailedComposeAsync(
                driver, context, config, _removeVolumes, borrowedProject, cleanupTimeout, Logger)
            .ConfigureAwait(false);
        CaptureFailedComposeService(ownedTempFiles, cleanedUp);
        RemoveComposeFiles(ownedTempFiles);
        DeleteTempFiles(ownedTempFiles);
        throw;
      }
      if (!response.Success)
      {
        var cleanedUp = await CleanupFailedComposeAsync(
                driver, context, config, _removeVolumes, borrowedProject, cleanupTimeout, Logger)
            .ConfigureAwait(false);
        CaptureFailedComposeService(ownedTempFiles, cleanedUp);
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
