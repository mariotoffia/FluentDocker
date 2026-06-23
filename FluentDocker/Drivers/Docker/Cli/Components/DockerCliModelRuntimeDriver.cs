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
    /// <summary>Initializes the driver with a binary resolver.</summary>
    /// <param name="binaryResolver">The binary resolver.</param>
    public DockerCliModelRuntimeDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelRunnerStatus>> StatusAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await RunAsync("model status", cancellationToken).ConfigureAwait(false);
        var output = result.Output ?? string.Empty;
        var running = output.Contains("is running", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("is not running", StringComparison.OrdinalIgnoreCase);

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
      if (options != null)
      {
        if (options.IgnoreRuntimeMemoryCheck)
          sb.Append(" --ignore-runtime-memory-check");
        if (options.Debug)
          sb.Append(" --debug");
        if (!string.IsNullOrEmpty(options.Backend))
          sb.Append(" --backend ").Append(QuoteArgumentIfNeeded(options.Backend));
      }

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

      if (!options.Backend.IsDefault)
        sb.Append(" --backend ").Append(QuoteArgumentIfNeeded(options.Backend.Name));

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
