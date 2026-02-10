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
  public partial class DockerCliComposeDriver : DockerCliDriverBase, IComposeDriver
  {
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliComposeDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, config.Environment, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ComposeUpResult>.Fail(
              result.Error ?? "Compose up failed",
              ErrorCodes.Compose.UpFailed,
              CreateErrorContext(context, "ComposeUp", result),
              result.ExitCode);
        }

        return CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult
        {
          ProjectName = config.ProjectName ?? "default",
          Services = config.Services.ToList()
        });
      }
      catch (Exception ex)
      {
        return CommandResponse<ComposeUpResult>.Fail(ex.Message, ErrorCodes.Compose.UpFailed);
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

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Compose down failed",
              ErrorCodes.Compose.DownFailed,
              CreateErrorContext(context, "ComposeDown", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.DownFailed);
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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose start failed", ErrorCodes.Compose.StartFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.StartFailed);
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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose stop failed", ErrorCodes.Compose.StopFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.StopFailed);
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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose restart failed", ErrorCodes.Compose.RestartFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.RestartFailed);
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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose pause failed", ErrorCodes.Compose.PauseFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.PauseFailed);
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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose unpause failed", ErrorCodes.Compose.UnpauseFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.UnpauseFailed);
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
        var args = BuildComposeArgs(config) + $" kill -s {config.Signal ?? "SIGKILL"}";
        if (config.Services.Count > 0)
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose kill failed", ErrorCodes.Compose.KillFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.KillFailed);
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
          args += " " + string.Join(" ", config.Services);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Compose rm failed", ErrorCodes.Compose.RemoveFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.RemoveFailed);
      }
    }

    #endregion

    #region Private Helpers

    private string BuildComposeArgs(ComposeFileConfig config)
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

    #endregion
  }
}
