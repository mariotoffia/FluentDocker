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
  public class DockerCliModelRuntimeDriver : DockerCliModelDriverBase, IModelRuntimeDriver, IModelBackendInfo
  {
    /// <summary>
    /// Internal time budget for the shared <c>docker model configure --help</c>
    /// capability probe. The probe runs the CLI itself; a wedged plugin must not hang an
    /// explicit-backend <c>ConfigureAsync</c> indefinitely, so the probe is abandoned (and
    /// the cached task evicted) after this elapses. Generous relative to a local <c>--help</c>
    /// invocation, small relative to a caller's patience. Overridable so tests can shrink it.
    /// </summary>
    protected virtual TimeSpan BackendProbeTimeout => TimeSpan.FromSeconds(5);

    // Cached per context host probe of whether `docker model configure` advertises
    // `--backend`. Only consulted when an explicit (non-auto) backend is requested. The probe
    // runs with its OWN internal-timeout token (never a caller's token) so no single caller can
    // cancel the shared task; callers observe it via WaitAsync(callerToken) so a caller
    // cancelling its own request never poisons the shared task. A task that ends
    // Canceled/Faulted (or times out) is evicted (re-probed next time).
    private readonly object _backendProbeGate = new();
    private readonly Dictionary<string, Task<bool>> _configureBackendSupported = new(StringComparer.Ordinal);

    /// <summary>Initializes the driver with a binary resolver.</summary>
    /// <param name="binaryResolver">The binary resolver.</param>
    public DockerCliModelRuntimeDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <summary>
    /// The Docker Model Runner's inference backend engine. DMR runs models on
    /// <c>llama.cpp</c>, so this adapter advertises it as the default backend.
    /// </summary>
    public string DefaultBackend => "llama.cpp";

    /// <summary>
    /// The inference backend engine(s) the Docker Model Runner can use. Today this is
    /// solely <c>llama.cpp</c>.
    /// </summary>
    public IReadOnlyList<string> AvailableBackends => new[] { "llama.cpp" };

    /// <summary>
    /// Returns whether the installed <c>docker model configure</c> exposes a
    /// <c>--backend</c> flag, by inspecting its <c>--help</c> output. Defaults to
    /// <c>false</c> (unsupported) when the probe itself fails, so an explicit backend
    /// never silently emits a flag we are unsure about.
    /// </summary>
    /// <param name="context">Per-call driver context used for the probe invocation.</param>
    /// <param name="cancellationToken">The CALLER'S token. Cancelling it abandons this
    /// caller's wait (surfacing <see cref="OperationCanceledException"/>) without cancelling
    /// the shared probe task — so one caller can never poison the cache for others.</param>
    /// <remarks>
    /// The per-context-host shared probe runs on its OWN
    /// <see cref="BackendProbeTimeout"/>-bounded token, never a caller's token, so no single
    /// caller can cancel (and thereby poison) the cached task. Each caller observes the
    /// shared task via <see cref="Task{TResult}.WaitAsync(CancellationToken)"/> bound to ITS
    /// token. As a second safety net, a cached task that ended Canceled/Faulted (e.g. a probe
    /// timeout) is evicted so the next caller re-probes rather than inheriting a dead task.
    /// </remarks>
    private async Task<bool> SupportsConfigureBackendAsync(DriverContext context, CancellationToken cancellationToken)
    {
      var key = BackendProbeCacheKey(context);
      Task<bool> probe;
      lock (_backendProbeGate)
      {
        if (!_configureBackendSupported.TryGetValue(key, out var cached) ||
            (cached.IsCompleted && (cached.IsCanceled || cached.IsFaulted)))
        {
          cached = ProbeConfigureBackendAsync(context);
          _configureBackendSupported[key] = cached;
        }

        probe = cached;
      }

      try
      {
        // Observe the shared probe under the caller's token only — a caller's cancellation
        // abandons this await (throws OperationCanceledException) but leaves the shared task
        // running for the next caller; it does not cancel the shared probe.
        return await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (probe.IsCanceled || probe.IsFaulted)
      {
        // The PROBE itself ended Canceled/Faulted (a seam fault, or its internal timeout
        // firing) — NOT merely this caller's wait. Evict it so a later call re-probes
        // rather than inheriting the dead task.
        EvictBackendProbe(key, probe);

        // A genuine caller cancellation is still a caller cancellation — honor it as OCE.
        if (cancellationToken.IsCancellationRequested)
          throw;

        // Otherwise the PROBE's own internal timeout (or a seam fault) fired while this
        // caller never cancelled (C-M2): surfacing OperationCanceledException here would
        // let a retry loop mistake a wedged Docker model plugin for a transient
        // cancellation and retry forever. Translate it into a typed, non-transient
        // failure instead; the ConfigureAsync call site maps it to CommandResponse.Fail.
        throw new DriverException(
            $"Timed out probing 'docker model configure --help' after {FormatInvariant(BackendProbeTimeout.TotalSeconds, "0.###")}s; " +
            "the Docker model plugin may be wedged — cannot determine '--backend' support. Retry or restart Docker Model Runner.",
            ErrorCodes.Model.ConfigureFailed);
      }
    }

    private async Task<bool> ProbeConfigureBackendAsync(DriverContext context)
    {
      // Own internal-timeout token — independent of any caller. A wedged `--help` is
      // abandoned after BackendProbeTimeout; the resulting OperationCanceledException leaves
      // the task Canceled so SupportsConfigureBackendAsync evicts and re-probes it (the
      // failure is never cached permanently). Any non-cancellation probe error is the same
      // "unknown ⇒ unsupported" answer (false), which is safe to cache.
      using var timeout = new CancellationTokenSource(BackendProbeTimeout);
      try
      {
        var result = await RunAsync(context, "model configure --help", timeout.Token).ConfigureAwait(false);
        var help = $"{result.Output} {result.Error}";
        return help.Contains("--backend", StringComparison.Ordinal);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return false;
      }
    }

    private static string BackendProbeCacheKey(DriverContext context) => context?.Host ?? string.Empty;

    private void EvictBackendProbe(string key, Task<bool> faulted)
    {
      lock (_backendProbeGate)
      {
        if (_configureBackendSupported.TryGetValue(key, out var cached) && ReferenceEquals(cached, faulted))
          _configureBackendSupported.Remove(key);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelRunnerStatus>> StatusAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await RunAsync(context, "model status", cancellationToken).ConfigureAwait(false);
        var output = result.Output ?? string.Empty;
        var combined = $"{output} {result.Error}";

        // Match the runner's specific phrasing ("Docker Model Runner is [not] running"),
        // NOT a loose "not running" substring — an unrelated error that happens to contain
        // those words must not be misread as a clean stopped state.
        var knownNotRunning = combined.Contains("model runner is not running", StringComparison.OrdinalIgnoreCase);
        var running = !knownNotRunning
            && combined.Contains("model runner is running", StringComparison.OrdinalIgnoreCase);

        // Output we cannot classify as running OR not-running is either a genuine failure
        // (docker missing, not permitted, plugin error) or CLI-wording drift. Either way, fail loud
        // with a typed status error instead of silently defaulting to Running=false (DMR-MAJ-3).
        if (!running && !knownNotRunning)
          return CommandResponse<ModelRunnerStatus>.Fail(
              ModelErrorOrDefault(result,
                  "could not determine model runner status from `docker model status` output (CLI format may have changed)"),
              ModelFailureCode(FirstNonEmpty(result.Error, output), ErrorCodes.Model.StatusFailed),
              result.ExitCode);

        return CommandResponse<ModelRunnerStatus>.Ok(new ModelRunnerStatus
        {
          Running = running,
          // Report the endpoint this context is actually bound to, falling back to the default
          // only when nothing is configured — otherwise status could claim localhost:12434 while
          // inference really targets the context's configured endpoint.
          Endpoint = (context?.ModelRunnerEndpoint ?? ModelRunnerEndpoint.Default()).BaseAddress,
          Error = running ? null : FirstNonEmpty(result.Error, output)
        });
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelRunnerStatus>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.StatusFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelRunnerVersion>> VersionAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await RunAsync(context, "model version", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ModelRunnerVersion>.Fail(
              ModelErrorOrDefault(result, "model version failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.VersionFailed),
              CreateErrorContext(context, "ModelVersion", result),
              result.ExitCode);

        return CommandResponse<ModelRunnerVersion>.Ok(ModelJsonParser.ParseVersion(result.Output));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelRunnerVersion>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.VersionFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<RunningModel>>> ListRunningAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // DMR `ps` is table-only (no `--json`).
        var result = await RunAsync(context, "model ps", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<RunningModel>>.Fail(
              ModelErrorOrDefault(result, "model ps failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.ListFailed),
              CreateErrorContext(context, "ListRunningModels", result),
              result.ExitCode);

        if (!ModelJsonParser.TryParsePsTable(result.Output, out var runningModels))
          return CommandResponse<IList<RunningModel>>.Fail(
              "Unable to parse 'model ps' table output",
              ErrorCodes.Model.ListFailed,
              CreateErrorContext(context, "ListRunningModels", result),
              result.ExitCode);

        return CommandResponse<IList<RunningModel>>.Ok(runningModels);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<IList<RunningModel>>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.ListFailed));
      }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Honors the full <see cref="ModelRunOptions"/> contract. Run-supported fields
    /// (<see cref="ModelRunOptions.Debug"/>, <see cref="ModelRunOptions.OpenAiUrl"/>,
    /// <see cref="ModelRunOptions.WebSearch"/>) map onto <c>docker model run</c>;
    /// explicit <c>Detach=false</c> is rejected because it starts an interactive chat session.
    /// Configure-only fields
    /// (<see cref="ModelRunOptions.ContextSize"/>, <see cref="ModelRunOptions.RuntimeFlags"/>)
    /// have no <c>run</c> flag in DMR v1.2.1, so they are applied via
    /// <see cref="ConfigureAsync"/> just BEFORE the run rather than silently dropped; if that
    /// pre-configure fails its response is returned and the run is not attempted. No field
    /// with a CLI surface is ignored.
    /// </remarks>
    public async Task<CommandResponse<Unit>> LoadAsync(DriverContext context,
        ModelReference model, ModelRunOptions? options = null, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

      if (options is { Detach: false })
        return CommandResponse<Unit>.Fail(
            "ModelRunOptions.Detach=false would start an interactive 'docker model run' chat session that FluentDocker cannot consume. Use Detach=true (the default) to load the model for inference.",
            ErrorCodes.General.InvalidArgument);

      // Apply configure-only settings (no `run` flag exists for them) before the run so the
      // documented WithRunOptions contract is honored. Skip when nothing is configure-bound.
      if (options is not null && (options.ContextSize.HasValue ||
          (options.RuntimeFlags is { Count: > 0 })))
      {
        var configureResponse = await ConfigureAsync(context, model, new ModelConfigureOptions
        {
          ContextSize = options.ContextSize,
          RuntimeFlags = options.RuntimeFlags
        }, cancellationToken).ConfigureAwait(false);

        if (!configureResponse.Success)
          return configureResponse;
      }

      var sb = new StringBuilder("model run");

      // Detach defaults to true (load-for-later-inference); only the explicit run flags below
      // are emitted — `docker model run` v1.2.1 has no --context-size/--backend, those are
      // configure-only and were applied above.
      if (options is null || options.Detach)
        sb.Append(" -d");
      if (options is { Debug: true })
        sb.Append(" --debug");
      if (options is { WebSearch: true })
        sb.Append(" --websearch");
      if (!string.IsNullOrEmpty(options?.OpenAiUrl))
        sb.Append(" --openaiurl ").Append(QuoteArgumentIfNeeded(options.OpenAiUrl));

      sb.Append(' ').Append(QuoteArgumentIfNeeded(model.ToString()));
      // ponytail: model run -d may implicitly pull, so only caller cancellation bounds it.
      return await SimpleUnitAsync(context, sb.ToString(), "LoadModel", ErrorCodes.Model.LoadFailed, cancellationToken, unbounded: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnloadAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

      var args = $"model unload {QuoteArgumentIfNeeded(model.ToString())}";
      return await SimpleUnitAsync(context, args, "UnloadModel", ErrorCodes.Model.UnloadFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnloadAllAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      return await SimpleUnitAsync(context, "model unload --all", "UnloadAllModels", ErrorCodes.Model.UnloadFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ConfigureAsync(DriverContext context,
        ModelReference model, ModelConfigureOptions options, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

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
        bool backendSupported;
        try
        {
          backendSupported = await SupportsConfigureBackendAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (DriverException ex) when (ex.ErrorCode == ErrorCodes.Model.ConfigureFailed)
        {
          // The shared probe itself timed out or faulted (C-M2) — surface it as a typed
          // Fail like every other failure this method returns, never a thrown exception.
          // A genuine caller cancellation is a separate, unfiltered OperationCanceledException.
          return CommandResponse<Unit>.Fail(ex.Message, ex.ErrorCode);
        }

        if (!backendSupported)
          return CommandResponse<Unit>.Fail(
              $"The installed 'docker model configure' does not support explicit backend selection ('--backend'); " +
              $"the backend is auto-selected from the model format. Use the default backend (\"auto\") or upgrade Docker Model Runner. (requested: '{options.Backend}')",
              ErrorCodes.Model.ConfigureFailed);

        sb.Append(" --backend ").Append(QuoteArgumentIfNeeded(options.Backend!));
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
    /// <remarks>
    /// A failed <c>docker model logs</c> (non-zero exit) or a mid-stream transport fault is
    /// translated into a typed <see cref="ModelRunnerException"/> carrying
    /// <see cref="ErrorCodes.Model.LogsFailed"/> (with the underlying message preserved),
    /// rather than the generic driver-level <see cref="DriverException"/> the streaming
    /// primitive throws — so model-log consumers can catch the model-specific failure.
    /// A genuine caller cancellation still surfaces as <see cref="OperationCanceledException"/>.
    /// </remarks>
    public async IAsyncEnumerable<string> LogsAsync(DriverContext context, bool follow = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var args = follow ? "model logs -f" : "model logs";
      var stream = RunStreamingAsync(context, args, cancellationToken).ConfigureAwait(false);
      await using var enumerator = stream.GetAsyncEnumerator();

      while (true)
      {
        bool moved;
        try
        {
          // Translate failures from the streaming primitive (a non-zero exit raises a generic
          // DriverException; a mid-stream transport fault surfaces here too) into the typed
          // model exception. OperationCanceledException is a caller cancellation — let it pass.
          moved = await enumerator.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Exception ex)
        {
          throw new ModelRunnerException(ex.Message, ErrorCodes.Model.LogsFailed, ex);
        }

        if (!moved)
          yield break;

        yield return enumerator.Current;
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> InstallRunnerAsync(DriverContext context,
        ModelRunnerInstallOptions? options = null, CancellationToken cancellationToken = default)
    {
      var args = "model install-runner";
      if (!string.IsNullOrEmpty(options?.Gpu))
        args += $" --gpu {QuoteArgumentIfNeeded(options.Gpu)}";

      return await SimpleUnitAsync(context, args, "InstallRunner", ErrorCodes.Model.InstallFailed, cancellationToken, unbounded: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UninstallRunnerAsync(DriverContext context,
        ModelRunnerUninstallOptions? options = null, CancellationToken cancellationToken = default)
    {
      var sb = new StringBuilder("model uninstall-runner");
      if (options != null)
      {
        if (options.RemoveImages)
          sb.Append(" --images");
        if (options.RemoveModels)
          sb.Append(" --models");
      }

      return await SimpleUnitAsync(context, sb.ToString(), "UninstallRunner", ErrorCodes.Model.UninstallFailed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResponse<Unit>> SimpleUnitAsync(DriverContext context,
        string args, string operation, string errorCode, CancellationToken cancellationToken, bool unbounded = false)
    {
      try
      {
        var result = unbounded
            ? await RunUnboundedAsync(context, args, cancellationToken).ConfigureAwait(false)
            : await RunAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ModelErrorOrDefault(result, $"{operation} failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), errorCode),
              CreateErrorContext(context, operation, result),
              result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ModelFailureCode(ex, errorCode));
      }
    }
  }
}
