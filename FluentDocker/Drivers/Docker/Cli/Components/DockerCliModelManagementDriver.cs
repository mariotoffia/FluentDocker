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
        ModelReference model, IProgress<ModelPullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
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
            ErrorCodes.Model.PullFailed,
            info.ErrorContext,
            info.ExitCode);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.PullFailed));
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
              ErrorOrDefault(result, "model ls failed"),
              FailureCode(result.Error, ErrorCodes.Model.ListFailed),
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
        return CommandResponse<IList<ModelInfo>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.ListFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> InspectAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default)
    {
      try
      {
        // DMR v1.2.1 `model inspect` outputs JSON by default and rejects `--json`.
        var args = $"model inspect {QuoteArgumentIfNeeded(model.ToString())}";
        var result = await RunAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
          var error = FirstNonEmpty(result.Error, result.Output, "model inspect failed");
          return CommandResponse<ModelInfo>.Fail(
              error,
              IndicatesNoSuchModel(error) ? ErrorCodes.Model.NotFound : FailureCode(error, ErrorCodes.Model.InspectFailed),
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
        return CommandResponse<ModelInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(DriverContext context,
        ModelReference model, bool force = false, CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "model rm";
        if (force)
          args += " -f";
        args += $" {QuoteArgumentIfNeeded(model.ToString())}";

        var result = await RunAsync(context, args, cancellationToken).ConfigureAwait(false);

        // DMR `rm` of a missing model prints an error but exits 0 — inspect output.
        if (!result.Success || IndicatesRemoveFailure(result.Output))
          return CommandResponse<Unit>.Fail(
              FirstNonEmpty(result.Error, result.Output, "model rm failed"),
              FailureCode(FirstNonEmpty(result.Error, result.Output), ErrorCodes.Model.RemoveFailed),
              CreateErrorContext(context, "RemoveModel", result),
              result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.RemoveFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> TagAsync(DriverContext context,
        ModelReference source, ModelReference target, CancellationToken cancellationToken = default)
    {
      var args = $"model tag {QuoteArgumentIfNeeded(source.ToString())} {QuoteArgumentIfNeeded(target.ToString())}";
      return await SimpleUnitAsync(context, args, "TagModel", ErrorCodes.Model.TagFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default)
    {
      var args = $"model push {QuoteArgumentIfNeeded(model.ToString())}";
      return await SimpleUnitAsync(context, args, "PushModel", ErrorCodes.Model.PushFailed, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ModelInfo>> PackageAsync(DriverContext context,
        ModelPackageRequest request, CancellationToken cancellationToken = default)
    {
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

        var result = await RunAsync(context, sb.ToString(), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ModelInfo>.Fail(
              ErrorOrDefault(result, "model package failed"),
              FailureCode(result.Error, ErrorCodes.Model.PackageFailed),
              CreateErrorContext(context, "PackageModel", result),
              result.ExitCode);

        return CommandResponse<ModelInfo>.Ok(new ModelInfo { Reference = request.Target });
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.PackageFailed));
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
              ErrorOrDefault(result, "model purge failed"),
              FailureCode(result.Error, ErrorCodes.Model.PruneFailed),
              CreateErrorContext(context, "PruneModels", result),
              result.ExitCode);

        return CommandResponse<ModelPruneResult>.Ok(ModelJsonParser.ParsePruneResult(result.Output));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelPruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.PruneFailed));
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
              ErrorOrDefault(result, "model df failed"),
              FailureCode(result.Error, ErrorCodes.Model.DiskUsageFailed),
              CreateErrorContext(context, "ModelDiskUsage", result),
              result.ExitCode);

        return CommandResponse<ModelDiskUsage>.Ok(ModelJsonParser.ParseDfTable(result.Output));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ModelDiskUsage>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Model.DiskUsageFailed));
      }
    }

    private async Task<CommandResponse<Unit>> SimpleUnitAsync(DriverContext context,
        string args, string operation, string errorCode, CancellationToken cancellationToken)
    {
      try
      {
        var result = await RunAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, $"{operation} failed"),
              FailureCode(result.Error, errorCode),
              CreateErrorContext(context, operation, result),
              result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, errorCode));
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

    private static string FirstNonEmpty(params string[] values)
    {
      foreach (var value in values)
      {
        if (!string.IsNullOrWhiteSpace(value))
          return value;
      }

      return string.Empty;
    }
  }
}
