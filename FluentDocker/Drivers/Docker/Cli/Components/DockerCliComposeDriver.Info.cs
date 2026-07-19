using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI compose driver: information, build/pull, execution, and scale/copy operations.
  /// Create operations and exec-failure classification are in the <c>Create</c> partial file.
  /// </summary>
  public partial class DockerCliComposeDriver
  {
    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ComposeServiceInfo>>> ListAsync(
        DriverContext context,
        ComposeListConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildListSubArgs(config);

        var result = await ExecuteCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<IList<ComposeServiceInfo>>.Fail(
              ErrorOrDefault(result, "Compose ps failed"), FailureCode(result.Error, ErrorCodes.Compose.ListFailed));

        return TryParseServiceList(result.Output, Logger, out var services, out var parseError)
            ? CommandResponse<IList<ComposeServiceInfo>>.Ok(config.Quiet
                ? services.Select(s => new ComposeServiceInfo { ContainerId = s.ContainerId }).ToList()
                : services)
            : CommandResponse<IList<ComposeServiceInfo>>.Fail(parseError, ErrorCodes.Compose.ListFailed);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ComposeServiceInfo>>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Compose.ListFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        ComposeLogsConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        if (config.Follow)
        {
          return CommandResponse<string>.Fail(
              "Compose GetLogsAsync follow=true is not supported by this buffered method; use a streaming logs API instead.",
              ErrorCodes.Compose.LogsFailed);
        }

        var args = BuildComposeArgs(config) + " " + BuildLogsSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(
                ErrorOrDefault(result, "Compose logs failed"), FailureCode(result.Error, ErrorCodes.Compose.LogsFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.LogsFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ComposeProcesses>>> TopAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " top";
        var result = await ExecuteCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<ComposeProcesses>>.Fail(
              ErrorOrDefault(result, "Compose top failed"), FailureCode(result.Error, ErrorCodes.Compose.TopFailed));

        return CommandResponse<IList<ComposeProcesses>>.Ok(
            await ParseTopOutputWithServiceInfoAsync(context, config, result.Output, cancellationToken).ConfigureAwait(false));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ComposeProcesses>>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Compose.TopFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> ConfigAsync(
        DriverContext context,
        ComposeConfigConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildConfigSubArgs(config);

        var result = await ExecuteCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(
                ErrorOrDefault(result, "Compose config failed"), FailureCode(result.Error, ErrorCodes.Compose.ConfigFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.ConfigFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ComposeImage>>> ImagesAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " images --format json";
        var result = await ExecuteCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<IList<ComposeImage>>.Fail(
              ErrorOrDefault(result, "Compose images failed"), FailureCode(result.Error, ErrorCodes.Compose.ImagesFailed));

        var images = new List<ComposeImage>();
        var output = result.Output.Trim();

        // Docker Compose V2 may return a JSON array or NDJSON
        if (output.StartsWith('['))
        {
          try
          {
            var arr = JsonSerializer.Deserialize<List<ComposeImage>>(output, JsonHelper.CaseInsensitiveOptions);
            if (arr != null)
              images.AddRange(arr);
          }
          catch (Exception ex)
          {
            Logger.LogDebug(ex, "Compose images array JSON parsing failed");
            return CommandResponse<IList<ComposeImage>>.Fail(
                "Compose images array JSON parsing failed: " + ex.Message,
                ErrorCodes.Compose.ImagesFailed);
          }
        }
        else
        {
          if (!DockerCliJsonLineParser.TryParse<ComposeImage>(
              output,
              Logger,
              "Compose image line JSON parsing failed",
              out images,
              out var parseError))
            return CommandResponse<IList<ComposeImage>>.Fail(parseError, ErrorCodes.Compose.ImagesFailed);
        }

        return CommandResponse<IList<ComposeImage>>.Ok(images);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ComposeImage>>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Compose.ImagesFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> PortAsync(
        DriverContext context,
        ComposePortConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // --protocol is optional; an explicitly null/empty Protocol must not emit `--protocol ""`.
        var protocolArgs = string.IsNullOrEmpty(config.Protocol)
            ? string.Empty
            : $" --protocol {QuoteArgumentIfNeeded(config.Protocol)}";
        var args = BuildComposeArgs(config) +
            $" port{protocolArgs} {QuotePositionalArgument(config.Service, nameof(config.Service))} {FormatInvariant(config.PrivatePort)}";
        var result = await ExecuteCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output.Trim())
            : CommandResponse<string>.Fail(
                ErrorOrDefault(result, "Compose port failed"), FailureCode(result.Error, ErrorCodes.Compose.PortFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.PortFailed));
      }
    }

    #endregion

    #region Build/Pull Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> BuildAsync(
        DriverContext context,
        ComposeBuildConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildBuildSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Compose build failed"), FailureCode(result.Error, ErrorCodes.Compose.BuildFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.BuildFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PullAsync(
        DriverContext context,
        ComposePullConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildPullSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Compose pull failed"), FailureCode(result.Error, ErrorCodes.Compose.PullFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.PullFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " push";
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Compose push failed"), FailureCode(result.Error, ErrorCodes.Compose.PushFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.PushFailed));
      }
    }

    #endregion

    #region Execution Operations

    /// <inheritdoc />
    public async Task<CommandResponse<string>> ExecuteAsync(
        DriverContext context,
        ComposeExecConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " exec";
        if (config.Detach)
          args += " -d";
        if (!config.Tty)
          args += " -T";
        if (config.Privileged)
          args += " --privileged";
        if (!string.IsNullOrEmpty(config.User))
          args += $" -u {QuoteArgumentIfNeeded(config.User)}";
        if (!string.IsNullOrEmpty(config.WorkDir))
          args += $" -w {QuoteArgumentIfNeeded(config.WorkDir)}";
        if (config.Index.HasValue)
          args += $" --index {FormatInvariant(config.Index.Value)}";
        args += $" {QuotePositionalArgument(config.Service, nameof(config.Service))}";
        if (config.Command is { Length: > 0 })
          args += " " + string.Join(" ", config.Command.Select(QuoteArgumentIfNeeded));

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        if (IsComposeExecInfrastructureFailure(result.ExitCode, result.Output, result.Error))
        {
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Compose exec failed"),
              FailureCode(result.Error, ErrorCodes.Compose.ExecFailed),
              CreateErrorContext(context, "ComposeExec", result),
              result.ExitCode);
        }

        return CommandResponse<string>.Ok(result.Output, MergeOutputAndError(result.Output, result.Error), result.ExitCode);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.ExecFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> RunAsync(
        DriverContext context,
        ComposeRunConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildRunSubArgs(config);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        if (IsComposeExecInfrastructureFailure(result.ExitCode, result.Output, result.Error))
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Compose run failed"),
              FailureCode(result.Error, ErrorCodes.Compose.RunFailed),
              CreateErrorContext(context, "ComposeRun", result),
              result.ExitCode);

        // Data = stdout only: compose run writes its own progress (network/pull chatter)
        // to stderr, which must not pollute the command's parsed output. Output = merged.
        return CommandResponse<string>.Ok(result.Output, MergeOutputAndError(result.Output, result.Error), result.ExitCode);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.RunFailed));
      }
    }

    #endregion

    #region Scale/Copy Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context,
        ComposeScaleConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildScaleSubArgs(config);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Compose scale failed"), FailureCode(result.Error, ErrorCodes.Compose.ScaleFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.ScaleFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyAsync(
        DriverContext context,
        ComposeCopyConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " cp";
        if (config.Archive)
          args += " -a";
        if (config.FollowLinks)
          args += " -L";
        if (config.Index.HasValue)
          args += $" --index {FormatInvariant(config.Index.Value)}";
        args += $" {QuotePositionalArgument(config.Source, nameof(config.Source))} {QuotePositionalArgument(config.Destination, nameof(config.Destination))}";

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Compose cp failed"), FailureCode(result.Error, ErrorCodes.Compose.CopyFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.CopyFailed));
      }
    }

    #endregion
  }
}
