#nullable disable warnings
using System;
using System.Collections.Generic;
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
  /// Docker Model Runner CLI adapter for model distribution / local-store
  /// operations (<c>docker model pull/ls/inspect/rm/tag/push/package/purge/df</c>).
  /// </summary>
  public class DockerCliModelManagementDriver : DockerCliModelDriverBase, IModelManagementDriver
  {
    /// <summary>Initializes the driver with a binary resolver.</summary>
    /// <param name="binaryResolver">The binary resolver.</param>
    public DockerCliModelManagementDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> PullAsync(DriverContext context,
        ModelReference model, IProgress<ModelPullProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

      try
      {
        // `docker model pull` writes its progress (the "X of Y" / percent lines) to
        // STDERR, not stdout — so use the progress-capable streaming path that
        // interleaves stderr, otherwise almost no progress would ever be reported.
        var args = $"model pull {QuoteArgumentIfNeeded(model.ToString())}";
        await foreach (var line in RunStreamingWithProgressAsync(context, args, cancellationToken).ConfigureAwait(false))
        {
          var update = ModelJsonParser.ParsePullLine(line);
          if (update != null)
            progress?.Report(update);
        }

        var info = await InspectAsync(context, model, cancellationToken).ConfigureAwait(false);
        if (info.Success)
          return info;

        return CommandResponse<ModelInfo>.Fail(
            string.IsNullOrEmpty(info.Error) ? "model pull failed" : info.Error,
            PullFailureCode(info.Error),
            info.ErrorContext,
            info.ExitCode);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, PullFailureCode(ex));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ModelInfo>>> ListAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await RunAsync(context, "model ls --json", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<ModelInfo>>.Fail(
              ModelErrorOrDefault(result, "model ls failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.ListFailed),
              CreateErrorContext(context, "ListModels", result),
              result.ExitCode);

        if (!ModelJsonParser.TryParseList(result.Output, out var models))
          return CommandResponse<IList<ModelInfo>>.Fail(
              "Unable to parse 'model ls --json' output",
              ErrorCodes.Model.ListFailed,
              CreateErrorContext(context, "ListModels", result),
              result.ExitCode);

        return CommandResponse<IList<ModelInfo>>.Ok(models);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<IList<ModelInfo>>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.ListFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> InspectAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

      try
      {
        // DMR v1.2.1 `model inspect` outputs JSON by default and rejects `--json`.
        var args = $"model inspect {QuoteArgumentIfNeeded(model.ToString())}";
        var result = await RunAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
          var error = ModelErrorOrDefault(result, "model inspect failed");
          return CommandResponse<ModelInfo>.Fail(
              error,
              IndicatesNoSuchModel(error) ? ErrorCodes.Model.NotFound : ModelFailureCode(error, ErrorCodes.Model.InspectFailed),
              CreateErrorContext(context, "InspectModel", result),
              result.ExitCode);
        }

        var info = ModelJsonParser.ParseInfo(result.Output);
        if (info == null)
          return CommandResponse<ModelInfo>.Fail(
              "Unable to parse model inspect output",
              ErrorCodes.Model.InspectFailed,
              CreateErrorContext(context, "InspectModel", result),
              result.ExitCode);

        return CommandResponse<ModelInfo>.Ok(info);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(DriverContext context,
        ModelReference model, bool force = false, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

      try
      {
        var args = "model rm";
        if (force)
          args += " -f";
        args += $" {QuoteArgumentIfNeeded(model.ToString())}";

        var result = await RunAsync(context, args, cancellationToken).ConfigureAwait(false);

        // DMR `rm` of a missing model prints an error but exits 0 — inspect output. Scan BOTH
        // streams: some DMR versions emit the failure to stderr with exit 0, which stdout-only
        // detection would report as a successful removal — silent state divergence (DMR-MAJ-2).
        if (!result.Success || IndicatesRemoveFailure(FirstNonEmpty(result.Error, result.Output)))
          return CommandResponse<Unit>.Fail(
              ModelErrorOrDefault(result, "model rm failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.RemoveFailed),
              CreateErrorContext(context, "RemoveModel", result),
              result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.RemoveFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> TagAsync(DriverContext context,
        ModelReference source, ModelReference target, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(target);

      var args = $"model tag {QuoteArgumentIfNeeded(source.ToString())} {QuoteArgumentIfNeeded(target.ToString())}";
      return await SimpleUnitAsync(context, args, "TagModel", ErrorCodes.Model.TagFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(model);

      var args = $"model push {QuoteArgumentIfNeeded(model.ToString())}";
      return await SimpleUnitAsync(context, args, "PushModel", ErrorCodes.Model.PushFailed, cancellationToken, unbounded: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> PackageAsync(DriverContext context,
        ModelPackageRequest request, CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(request);

      try
      {
        var sb = new StringBuilder("model package");
        if (!string.IsNullOrEmpty(request.GgufPath))
          sb.Append(" --gguf ").Append(QuoteArgumentIfNeeded(request.GgufPath));
        if (!string.IsNullOrEmpty(request.License))
          sb.Append(" --license ").Append(QuoteArgumentIfNeeded(request.License));
        if (request.Push)
          sb.Append(" --push");

        if (request.Target != null)
          sb.Append(' ').Append(QuoteArgumentIfNeeded(request.Target.ToString()));

        var result = await RunUnboundedAsync(context, sb.ToString(), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ModelInfo>.Fail(
              ModelErrorOrDefault(result, "model package failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.PackageFailed),
              CreateErrorContext(context, "PackageModel", result),
              result.ExitCode);

        return CommandResponse<ModelInfo>.Ok(new ModelInfo { Reference = request.Target });
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.PackageFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelPruneResult>> PurgeAllAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // DMR has no `model prune` (it would print top-level help and exit 0 — a
        // false success). The actual verb is `model purge`, which removes ALL models;
        // `--force` keeps it non-interactive.
        var args = "model purge --force";

        var result = await RunAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ModelPruneResult>.Fail(
              ModelErrorOrDefault(result, "model purge failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.PruneFailed),
              CreateErrorContext(context, "PruneModels", result),
              result.ExitCode);

        return CommandResponse<ModelPruneResult>.Ok(ModelJsonParser.ParsePruneResult(result.Output));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelPruneResult>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.PruneFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelDiskUsage>> DiskUsageAsync(DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // DMR `df` is table-only (rejects `--json`).
        var result = await RunAsync(context, "model df", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ModelDiskUsage>.Fail(
              ModelErrorOrDefault(result, "model df failed"),
              ModelFailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.DiskUsageFailed),
              CreateErrorContext(context, "ModelDiskUsage", result),
              result.ExitCode);

        if (!ModelJsonParser.TryParseDfTable(result.Output, out var usage))
          return CommandResponse<ModelDiskUsage>.Fail(
              "Unable to parse 'model df' table output",
              ErrorCodes.Model.DiskUsageFailed,
              CreateErrorContext(context, "ModelDiskUsage", result),
              result.ExitCode);

        return CommandResponse<ModelDiskUsage>.Ok(usage);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelDiskUsage>.Fail(ex.Message, ModelFailureCode(ex, ErrorCodes.Model.DiskUsageFailed));
      }
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

    private static bool IndicatesRemoveFailure(string output)
    {
      if (string.IsNullOrEmpty(output))
        return false;

      return IndicatesNoSuchModel(output)
          || output.Contains("Failed to remove", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IndicatesNoSuchModel(string output) =>
        !string.IsNullOrEmpty(output) &&
        output.Contains("no such model", StringComparison.OrdinalIgnoreCase);

    private static string PullFailureCode(string error)
    {
      // ponytail: stderr heuristics stop at "missing model"; add auth/disk-full only when DMR gives stable text.
      return IndicatesNoSuchModel(error)
          ? ErrorCodes.Model.NotFound
          : ModelFailureCode(error, ErrorCodes.Model.PullFailed);
    }

    private static string PullFailureCode(Exception ex) =>
        IndicatesNoSuchModel(ex?.Message)
            ? ErrorCodes.Model.NotFound
            : ModelFailureCode(ex, ErrorCodes.Model.PullFailed);

  }
}
