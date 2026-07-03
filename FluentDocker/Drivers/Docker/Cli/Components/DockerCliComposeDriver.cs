using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IComposeDriver.
  /// Lifecycle operations are in this file; info/query operations are in the partial class.
  /// </summary>
  /// <remarks>
  /// Creates a new instance with the specified binary resolver.
  /// </remarks>
  public partial class DockerCliComposeDriver(IBinaryResolver binaryResolver) : DockerCliDriverBase(binaryResolver), IComposeDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    private static readonly char[] NewlineSeparator = ['\n'];

    #region Lifecycle Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ComposeUpResult>> UpAsync(
        DriverContext context,
        ComposeUpConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config);
        if (config.Profiles != null && config.Profiles.Count > 0)
          foreach (var profile in config.Profiles)
            args += $" --profile {QuoteArgumentIfNeeded(profile)}";
        args += " " + BuildUpSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ComposeUpResult>.Fail(
              ErrorOrDefault(result, "Compose up failed"),
              FailureCode(result.Error, ErrorCodes.Compose.UpFailed),
              CreateErrorContext(context, "ComposeUp", result),
              result.ExitCode);
        }

        return CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult
        {
          ProjectName = config.ProjectName ?? "default",
          Services = [.. config.Services]
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ComposeUpResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.UpFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> DownAsync(
        DriverContext context,
        ComposeDownConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildDownSubArgs(config);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Compose down failed"),
              FailureCode(result.Error, ErrorCodes.Compose.DownFailed),
              CreateErrorContext(context, "ComposeDown", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.DownFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " start";
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose start failed"), FailureCode(result.Error, ErrorCodes.Compose.StartFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.StartFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopAsync(
        DriverContext context,
        ComposeStopConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " stop";
        if (config.Timeout.HasValue)
          args += $" --timeout {config.Timeout.Value}";
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose stop failed"), FailureCode(result.Error, ErrorCodes.Compose.StopFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.StopFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RestartAsync(
        DriverContext context,
        ComposeRestartConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildRestartSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose restart failed"), FailureCode(result.Error, ErrorCodes.Compose.RestartFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.RestartFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PauseAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " pause";
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose pause failed"), FailureCode(result.Error, ErrorCodes.Compose.PauseFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.PauseFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnpauseAsync(
        DriverContext context,
        ComposeFileConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " unpause";
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose unpause failed"), FailureCode(result.Error, ErrorCodes.Compose.UnpauseFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.UnpauseFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> KillAsync(
        DriverContext context,
        ComposeKillConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + $" kill -s {QuotePositionalArgument(config.Signal ?? "SIGKILL", nameof(config.Signal))}";
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose kill failed"), FailureCode(result.Error, ErrorCodes.Compose.KillFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.KillFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        ComposeRemoveConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildRemoveSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Compose rm failed"), FailureCode(result.Error, ErrorCodes.Compose.RemoveFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.RemoveFailed));
      }
    }

    #endregion

    #region Private Helpers

    private static string BuildComposeArgs(ComposeFileConfig config)
    {
      var args = "compose";
      foreach (var file in config.ComposeFiles)
        args += $" -f {QuoteArgumentIfNeeded(file)}";
      if (!string.IsNullOrEmpty(config.ProjectName))
        args += $" -p {QuoteArgumentIfNeeded(config.ProjectName)}";
      if (!string.IsNullOrEmpty(config.ProjectDirectory))
        args += $" --project-directory {QuoteArgumentIfNeeded(config.ProjectDirectory)}";
      return args;
    }

    private static string QuoteServices(IEnumerable<string> services) =>
        string.Join(" ", services.Select(service => QuotePositionalArgument(service, nameof(services))));

    #endregion
  }
}
