using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// A Docker Compose test resource with async lifecycle.
  /// </summary>
  public class ComposeResource : ResourceBase
  {
    private readonly Action<IComposeBuilder> _configure;
    private string? _sessionLabelOverlayPath;
    private bool _borrowedProject;
    private static readonly Action<ILogger, Exception> DiagnosticsLogCollectionFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(DiagnosticsLogCollectionFailed)),
            "Compose diagnostics log collection failed.");
    private static readonly Action<ILogger, string, Exception?> ComposeLabelOverlaySkipped =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(ComposeLabelOverlaySkipped)),
            "Compose session label overlay was not created: {Reason}");

    /// <summary>
    /// Creates a compose resource.
    /// </summary>
    /// <param name="kernel">Kernel with registered drivers.</param>
    /// <param name="configure">Compose builder configuration callback.</param>
    /// <param name="options">Optional resource options.</param>
    public ComposeResource(
        FluentDockerKernel kernel,
        Action<IComposeBuilder> configure,
        DockerResourceOptions? options = null)
        : base(kernel, options)
    {
      ArgumentNullException.ThrowIfNull(configure);
      _configure = configure;
    }

    /// <summary>
    /// The running compose service, available after initialization.
    /// </summary>
    public IComposeService? Service { get; private set; }

    /// <summary>
    /// Lists all services in the compose project.
    /// </summary>
    public Task<IList<ComposeServiceInfo>> ListServicesAsync(
        CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Service!.ListServicesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets compose logs.
    /// </summary>
    public Task<string> GetLogsAsync(CancellationToken cancellationToken = default)
    {
      EnsureInitialized();
      return Service!.GetLogsAsync(false, cancellationToken);
    }

    #region ResourceBase overrides

    /// <inheritdoc />
    protected override async Task PreflightAsync(CancellationToken cancellationToken)
    {
      await CapabilityChecks.EnsureComposeSupportAsync(Kernel, DriverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ProvisionAsync(CancellationToken cancellationToken)
    {
      var generation = ProvisionGeneration;
      _borrowedProject = false;
      var builder = new ComposeBuilder(Kernel, DriverId);
      _configure(builder);
      var callerProjectName = !string.IsNullOrWhiteSpace(builder._projectName);
      var attachToExisting = builder._attachToExisting;
      var ownsGeneratedProject = !callerProjectName && !attachToExisting;
      if (!callerProjectName && !attachToExisting)
        builder._projectName = GenerateUniqueName("compose");
      string? sessionLabelOverlayPath = null;
      if (Options.EnableSessionLabels)
      {
        sessionLabelOverlayPath = await CreateSessionLabelOverlayAsync(builder, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrEmpty(sessionLabelOverlayPath))
          builder.WithComposeFile(sessionLabelOverlayPath);
      }

      IServiceAsync result;
      try
      {
        result = await builder.ExecuteAsync(Options.TeardownTimeout, cancellationToken)
            .ConfigureAwait(false);
      }
      catch
      {
        DeleteSessionLabelOverlay(sessionLabelOverlayPath);
        throw;
      }

      if (result is IComposeService compose)
      {
        if (TryCommitProvision(generation, () =>
        {
          Service = compose;
          ResourceName = compose.ProjectName ?? compose.Name;
          _sessionLabelOverlayPath = sessionLabelOverlayPath;
          _borrowedProject = attachToExisting || builder.BorrowedProject;
        }))
        {
          return;
        }

        await RemoveStaleComposeAsync(
            compose, sessionLabelOverlayPath, generation, ownsGeneratedProject,
            attachToExisting || builder.BorrowedProject)
            .ConfigureAwait(false);
      }
      else
      {
        DeleteSessionLabelOverlay(sessionLabelOverlayPath);
        throw new InvalidOperationException("Builder did not produce a compose service");
      }
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync(CancellationToken cancellationToken)
    {
      if (Service == null)
        return;

      if (_borrowedProject)
        await Service.DisposeAsync().ConfigureAwait(false);
      else
      {
        await Service.StopAsync(cancellationToken).ConfigureAwait(false);
        await Service.RemoveAsync(force: false, cancellationToken).ConfigureAwait(false);
      }

      Service = null;
      _borrowedProject = false;
      DeleteSessionLabelOverlay();
    }

    /// <inheritdoc />
    protected override async Task ForceRemoveAsync(CancellationToken cancellationToken)
    {
      var s = Service;
      if (s == null)
        return;

      try
      {
        if (_borrowedProject)
          await s.DisposeAsync().ConfigureAwait(false);
        else
          await s.RemoveAsync(force: true, cancellationToken).ConfigureAwait(false);
        Service = null;
        _borrowedProject = false;
        DeleteSessionLabelOverlay();
      }
      catch (DriverException ex) when (IsNotFound(ex))
      {
        Service = null;
        _borrowedProject = false;
        DeleteSessionLabelOverlay();
      }
    }

    /// <inheritdoc />
    protected override async Task<ResourceDiagnostics> CollectDiagnosticsAsync(
        Exception failure,
        CancellationToken cancellationToken = default)
    {
      var diag = await base.CollectDiagnosticsAsync(failure, cancellationToken).ConfigureAwait(false);

      if (Service != null && Options.CaptureLogsOnFailure)
      {
        try
        {
          diag.Logs = TruncateLogLines(
              await Service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
          DiagnosticsLogCollectionFailed(Logger, ex);
          diag.Logs = "(failed to collect compose logs)";
        }
      }

      return diag;
    }

    #endregion

    private void EnsureInitialized()
    {
      if (!IsInitialized || Service == null)
        throw new InvalidOperationException(
            "Compose resource is not initialized. Call InitializeAsync first.");
    }

    private static bool IsNotFound(DriverException ex)
    {
      return ex.ErrorCode == ErrorCodes.Driver.NotFound ||
             ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> CreateSessionLabelOverlayAsync(
        ComposeBuilder builder,
        CancellationToken cancellationToken)
    {
      if (builder._attachToExisting)
        return null;

      try
      {
        // Per-user directory: on a multi-user CI host the shared temp root is sticky, so each
        // user must own its own top-level dir. A subdir under one shared "fluentdocker" parent
        // would inherit the first writer's permissions and make every other user's overlay write
        // fail with UnauthorizedAccessException — silently running compose resources UNLABELED and
        // invisible to orphan cleanup (TST-MAJ-3).
        var directory = Path.Combine(Path.GetTempPath(), $"fluentdocker-{CurrentUserDirSegment()}");
        SweepStaleSessionLabelOverlays(directory);
        await builder.LoadEnvFilesAsync(cancellationToken).ConfigureAwait(false);
        var environment = ComposeEnvironmentWithProfiles(builder);

        var driver = Kernel.SysCtl<IComposeDriver>(DriverId);
        var responseTask = driver.ConfigAsync(
            new DriverContext(DriverId),
            new ComposeConfigConfig
            {
              ComposeFiles = [.. builder._composeFiles],
              ProjectName = builder._projectName,
              Environment = environment,
              Format = "json"
            },
            cancellationToken);
        if (responseTask is null)
          return null;

        var response = await responseTask.ConfigureAwait(false);
        if (!response.Success)
        {
          ComposeLabelOverlaySkipped(Logger, response.Error ?? "compose config failed", null);
          return null;
        }

        if (!JsonHelper.TryDeserialize<JsonElement>(response.Data ?? string.Empty, out var root) ||
            root.ValueKind != JsonValueKind.Object)
        {
          ComposeLabelOverlaySkipped(Logger, "compose config did not return JSON", null);
          return null;
        }

        var overlay = BuildComposeLabelOverlay(root, SessionLabel.CreateLabels(Options.SessionId));
        if (overlay.Count == 0)
          return null;

        var path = Path.Combine(directory, $"compose-labels-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, JsonHelper.SerializeIndented(overlay), cancellationToken)
            .ConfigureAwait(false);
        return path;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        ComposeLabelOverlaySkipped(Logger, ex.Message, ex);
        return null;
      }
    }

    /// <summary>
    /// Filesystem-safe segment identifying the current OS user, so session-label overlays live
    /// under a per-user temp directory instead of a world-shared one.
    /// </summary>
    private static string CurrentUserDirSegment()
    {
      var user = Environment.UserName;
      if (string.IsNullOrEmpty(user))
        return "default";

      return new string(user
          .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')
          .ToArray());
    }

    private static void SweepStaleSessionLabelOverlays(string directory)
    {
      try
      {
        // ponytail: 24h cutoff, not a liveness check. Live resources delete their own overlay at
        // teardown (DeleteSessionLabelOverlay), so the only files that survive are crash-orphans.
        // The file mtime is frozen at creation, so age == time-since-creation; 24h stays clear of any
        // realistic test-fixture lifetime. Add a per-file heartbeat/refresh if fixtures ever run longer.
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);
        foreach (var path in Directory.EnumerateFiles(directory, "compose-labels-*.json"))
        {
          try
          {
            if (File.GetLastWriteTimeUtc(path) <= cutoff)
              File.Delete(path);
          }
          catch
          {
          }
        }
      }
      catch
      {
      }
    }

    private static Dictionary<string, object> BuildComposeLabelOverlay(
        JsonElement root,
        Dictionary<string, string> labels)
    {
      var overlay = new Dictionary<string, object>();
      AddLabelOverlaySection(overlay, "services", ComposeObjectNames(root, "services", false), labels);
      AddLabelOverlaySection(overlay, "networks", ComposeObjectNames(root, "networks", true), labels);
      AddLabelOverlaySection(overlay, "volumes", ComposeObjectNames(root, "volumes", true), labels);
      return overlay;
    }

    private static Dictionary<string, string> ComposeEnvironmentWithProfiles(ComposeBuilder builder)
    {
      var environment = new Dictionary<string, string>(
          builder._environment);
      if (builder._profiles.Count == 0)
        return environment;

      if (environment.TryGetValue("COMPOSE_PROFILES", out var existing) &&
          !string.IsNullOrWhiteSpace(existing))
      {
        environment["COMPOSE_PROFILES"] = existing + "," + string.Join(",", builder._profiles);
      }
      else
      {
        environment["COMPOSE_PROFILES"] = string.Join(",", builder._profiles);
      }

      return environment;
    }

    private static void AddLabelOverlaySection(
        Dictionary<string, object> overlay,
        string sectionName,
        IEnumerable<string> names,
        Dictionary<string, string> labels)
    {
      var section = new Dictionary<string, object>();
      foreach (var name in names.Distinct(StringComparer.Ordinal))
      {
        section[name] = new Dictionary<string, object>
        {
          ["labels"] = new Dictionary<string, string>(labels)
        };
      }

      if (section.Count > 0)
        overlay[sectionName] = section;
    }

    private static IEnumerable<string> ComposeObjectNames(
        JsonElement root,
        string propertyName,
        bool skipExternal)
    {
      if (!root.TryGetProperty(propertyName, out var section) ||
          section.ValueKind != JsonValueKind.Object)
        return [];

      return section.EnumerateObject()
          .Where(property => !skipExternal || !IsExternal(property.Value))
          .Select(property => property.Name);
    }

    private static bool IsExternal(JsonElement value)
    {
      return value.ValueKind == JsonValueKind.Object &&
             value.TryGetProperty("external", out var external) &&
             external.ValueKind == JsonValueKind.True;
    }

    private void DeleteSessionLabelOverlay()
    {
      var path = _sessionLabelOverlayPath;
      _sessionLabelOverlayPath = null;
      DeleteSessionLabelOverlay(path);
    }

    private static void DeleteSessionLabelOverlay(string? path)
    {
      if (string.IsNullOrEmpty(path))
        return;

      try
      {
        if (File.Exists(path))
          File.Delete(path);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }

    private async Task RemoveStaleComposeAsync(
        IComposeService compose,
        string? sessionLabelOverlayPath,
        int generation,
        bool ownsGeneratedProject,
        bool borrowedProject)
    {
      if (borrowedProject ||
          (!ownsGeneratedProject && !ShouldCleanupRejectedProvision(generation)))
      {
        DeleteSessionLabelOverlay(sessionLabelOverlayPath);
        return;
      }

      try
      {
        using var cts = new CancellationTokenSource(Options.TeardownTimeout);
        var removeTask = compose.RemoveAsync(force: true, cts.Token);
        await removeTask.WaitAsync(cts.Token).ConfigureAwait(false);
      }
      catch
      {
        OrphanCleanup.MarkAbandonedLateProvision(compose.ProjectName ?? compose.Name, Options.SessionId);
      }
      finally
      {
        DeleteSessionLabelOverlay(sessionLabelOverlayPath);
      }
    }
  }
}
