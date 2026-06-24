using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components.Parsing;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker Model Runner CLI adapter for runner control-plane operations
  /// (<c>docker model status/version/ps/run/unload/configure/logs/install-runner/
  /// uninstall-runner</c>).
  /// </summary>
  public class DockerCliModelRuntimeDriver : DockerCliModelDriverBase, IModelRuntimeDriver
  {
    // Cached probe of whether `docker model configure` advertises `--backend`. Only
    // consulted when an explicit (non-auto) backend is requested. The probe runs with
    // CancellationToken.None so no single caller can cancel the shared task; a task that
    // ends Canceled/Faulted is evicted (re-probed next time) instead of poisoning callers.
    private readonly object _backendProbeGate = new();
    private Task<bool> _configureBackendSupported;

    /// <summary>Initializes the driver with a binary resolver.</summary>
    /// <param name="binaryResolver">The binary resolver.</param>
    public DockerCliModelRuntimeDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <summary>
    /// Returns (and caches) whether the installed <c>docker model configure</c> exposes a
    /// <c>--backend</c> flag, by inspecting its <c>--help</c> output. Defaults to
    /// <c>false</c> (unsupported) when the probe itself fails, so an explicit backend
    /// never silently emits a flag we are unsure about.
    /// </summary>
    /// <remarks>
    /// The shared probe is run with <see cref="CancellationToken.None"/> so a single
    /// caller cancelling its own request can never cancel (and thereby poison) the cached
    /// task for every other caller. As a second safety net, a cached task that ended up
    /// Canceled or Faulted is evicted so the next caller re-probes rather than inheriting
    /// a dead task.
    /// </remarks>
    private Task<bool> SupportsConfigureBackendAsync()
    {
      lock (_backendProbeGate)
      {
        var cached = _configureBackendSupported;
        if (cached is null || (cached.IsCompleted && (cached.IsCanceled || cached.IsFaulted)))
          cached = _configureBackendSupported = ProbeConfigureBackendAsync(CancellationToken.None);

        return cached;
      }
    }

    private async Task<bool> ProbeConfigureBackendAsync(CancellationToken cancellationToken)
    {
      try
      {
        var result = await RunAsync("model configure --help", cancellationToken).ConfigureAwait(false);
        var help = $"{result.Output} {result.Error}";
        return help.Contains("--backend", StringComparison.Ordinal);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return false;
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelRunnerStatus>> StatusAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await RunAsync("model status", cancellationToken).ConfigureAwait(false);
        var output = result.Output ?? string.Empty;
        var combined = $"{output} {result.Error}";

        // Match the runner's specific phrasing ("Docker Model Runner is [not] running"),
        // NOT a loose "not running" substring — an unrelated error that happens to contain
        // those words must not be misread as a clean stopped state.
        var knownNotRunning = combined.Contains("model runner is not running", StringComparison.OrdinalIgnoreCase);
        var running = !knownNotRunning
            && combined.Contains("model runner is running", StringComparison.OrdinalIgnoreCase);

        // A non-zero exit that is NOT a recognizable running/not-running status is a
        // genuine failure (docker missing, not permitted, plugin error) — surface it
        // rather than silently reporting Running=false.
        if (!result.Success && !running && !knownNotRunning)
          return CommandResponse<ModelRunnerStatus>.Fail(
              FirstNonEmpty(result.Error, output, "docker model status failed"),
              ErrorCodes.Model.StatusFailed,
              result.ExitCode);

        return CommandResponse<ModelRunnerStatus>.Ok(new ModelRunnerStatus
        {
          Running = running,
          Endpoint = ModelRunnerEndpoint.Default().BaseAddress,
          Error = running ? null : FirstNonEmpty(result.Error, output)
        });
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelRunnerStatus>.Fail(ex.Message, ErrorCodes.Model.StatusFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelRunnerVersion>> VersionAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await RunAsync("model version", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ModelRunnerVersion>.Fail(
              result.Error ?? "model version failed",
              ErrorCodes.Model.VersionFailed,
              CreateErrorContext(context, "ModelVersion", result),
              result.ExitCode);

        return CommandResponse<ModelRunnerVersion>.Ok(ModelJsonParser.ParseVersion(result.Output));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelRunnerVersion>.Fail(ex.Message, ErrorCodes.Model.VersionFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<RunningModel>>> ListRunningAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // DMR `ps` is table-only (no `--json`).
        var result = await RunAsync("model ps", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<RunningModel>>.Fail(
              result.Error ?? "model ps failed",
              ErrorCodes.Model.ListFailed,
              CreateErrorContext(context, "ListRunningModels", result),
              result.ExitCode);

        return CommandResponse<IList<RunningModel>>.Ok(ModelJsonParser.ParsePsTable(result.Output));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<IList<RunningModel>>.Fail(ex.Message, ErrorCodes.Model.ListFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> LoadAsync(DriverContext context,
        ModelReference model, ModelRunOptions options = null, CancellationToken cancellationToken = default)
    {
      var sb = new StringBuilder("model run -d");
      if (options is { Debug: true })
        sb.Append(" --debug");

      sb.Append(' ').Append(QuoteArgumentIfNeeded(model.ToString()));
      return await SimpleUnitAsync(context, sb.ToString(), "LoadModel", ErrorCodes.Model.LoadFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnloadAsync(DriverContext context,
        ModelReference model, bool all = false, CancellationToken cancellationToken = default)
    {
      var args = all
          ? "model unload --all"
          : $"model unload {QuoteArgumentIfNeeded(model.ToString())}";

      return await SimpleUnitAsync(context, args, "UnloadModel", ErrorCodes.Model.UnloadFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ConfigureAsync(DriverContext context,
        ModelReference model, ModelConfigureOptions options, CancellationToken cancellationToken = default)
    {
      if (options == null)
        return CommandResponse<Unit>.Fail("configure options are required", ErrorCodes.Model.ConfigureFailed);

      var sb = new StringBuilder("model configure");

      if (options.ResetContextSize)
        sb.Append(" --context-size -1");
      else if (options.ContextSize.HasValue)
        sb.Append(" --context-size ").Append(options.ContextSize.Value.ToString(CultureInfo.InvariantCulture));

      if (!options.IsAutoBackend)
      {
        // `--backend` is auto/implicit on current DMR (engine chosen from model format).
        // Only emit an explicit backend when the installed CLI actually advertises the
        // flag — otherwise fail clearly rather than send a flag the CLI would reject.
        if (!await SupportsConfigureBackendAsync().ConfigureAwait(false))
          return CommandResponse<Unit>.Fail(
              $"The installed 'docker model configure' does not support explicit backend selection ('--backend'); " +
              $"the backend is auto-selected from the model format. Use the default backend (\"auto\") or upgrade Docker Model Runner. (requested: '{options.Backend}')",
              ErrorCodes.Model.ConfigureFailed);

        sb.Append(" --backend ").Append(QuoteArgumentIfNeeded(options.Backend));
      }

      if (!string.IsNullOrEmpty(options.HfOverridesJson))
        sb.Append(" --hf_overrides ").Append(QuoteArgumentIfNeeded(options.HfOverridesJson));

      sb.Append(' ').Append(QuoteArgumentIfNeeded(model.ToString()));

      if (options.RuntimeFlags != null && options.RuntimeFlags.Count > 0)
      {
        sb.Append(" -- ");
        sb.Append(string.Join(" ", options.RuntimeFlags.Select(QuoteArgumentIfNeeded)));
      }

      return await SimpleUnitAsync(context, sb.ToString(), "ConfigureModel", ErrorCodes.Model.ConfigureFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> LogsAsync(DriverContext context, bool follow = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = follow ? "model logs -f" : "model logs";
      await foreach (var line in RunStreamingAsync(args, cancellationToken).ConfigureAwait(false))
        yield return line;
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> InstallRunnerAsync(DriverContext context,
        ModelRunnerInstallOptions options = null, CancellationToken cancellationToken = default)
    {
      var args = "model install-runner";
      if (!string.IsNullOrEmpty(options?.Gpu))
        args += $" --gpu {QuoteArgumentIfNeeded(options.Gpu)}";

      return await SimpleUnitAsync(context, args, "InstallRunner", ErrorCodes.Model.InstallFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uninstalls the runner on Docker Engine CE (<c>docker model uninstall-runner</c>);
    /// used for upgrades. Not part of <see cref="IModelRuntimeDriver"/>.
    /// </summary>
    /// <param name="context">The driver context.</param>
    /// <param name="options">Uninstall options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    public async Task<CommandResponse<Unit>> UninstallRunnerAsync(DriverContext context,
        ModelRunnerUninstallOptions options = null, CancellationToken cancellationToken = default)
    {
      var sb = new StringBuilder("model uninstall-runner");
      if (options != null)
      {
        if (options.RemoveImages)
          sb.Append(" --images");
        if (options.RemoveModels)
          sb.Append(" --models");
      }

      return await SimpleUnitAsync(context, sb.ToString(), "UninstallRunner", ErrorCodes.Model.InstallFailed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResponse<Unit>> SimpleUnitAsync(DriverContext context,
        string args, string operation, string errorCode, CancellationToken cancellationToken)
    {
      try
      {
        var result = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? $"{operation} failed",
              errorCode,
              CreateErrorContext(context, operation, result),
              result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, errorCode);
      }
    }

    private static string FirstNonEmpty(params string[] values)
    {
      foreach (var value in values)
      {
        if (!string.IsNullOrWhiteSpace(value))
          return value;
      }

      return null;
    }
  }
}
