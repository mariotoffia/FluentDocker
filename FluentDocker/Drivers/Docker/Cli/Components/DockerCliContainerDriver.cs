using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IContainerDriver.
  /// </summary>
  /// <remarks>
  /// Creates a new instance with the specified binary resolver.
  /// </remarks>
  public partial class DockerCliContainerDriver(IBinaryResolver binaryResolver) : DockerCliDriverBase(binaryResolver), IContainerDriver
  {

    #region Lifecycle Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context,
        ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        if (string.IsNullOrEmpty(config.Image))
        {
          return CommandResponse<ContainerCreateResult>.Fail(
              "Container image is required but was not specified",
              ErrorCodes.Container.CreateFailed);
        }

        var args = BuildCreateArgs("create", config);

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerCreateResult>.Fail(
              result.Error ?? "Container creation failed",
              ErrorCodes.Container.CreateFailed,
              CreateErrorContext(context, "CreateContainer", result),
              result.ExitCode);
        }

        var containerId = result.Output.Trim();

        return CommandResponse<ContainerCreateResult>.Ok(
            new ContainerCreateResult { Id = containerId });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerCreateResult>.Fail(
            ex.Message,
            ErrorCodes.Container.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"start {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container start failed",
              ErrorCodes.Container.StartFailed,
              CreateErrorContext(context, "StartContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StartFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopAsync(
        DriverContext context,
        string containerId,
        int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "stop";
        if (timeout.HasValue)
          args += $" -t {timeout.Value}";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container stop failed",
              ErrorCodes.Container.StopFailed,
              CreateErrorContext(context, "StopContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StopFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RestartAsync(
        DriverContext context,
        string containerId,
        int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "restart";
        if (timeout.HasValue)
          args += $" -t {timeout.Value}";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container restart failed",
              ErrorCodes.Container.RestartFailed,
              CreateErrorContext(context, "RestartContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RestartFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PauseAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"pause {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container pause failed",
              ErrorCodes.Container.PauseFailed,
              CreateErrorContext(context, "PauseContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.PauseFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnpauseAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"unpause {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container unpause failed",
              ErrorCodes.Container.UnpauseFailed,
              CreateErrorContext(context, "UnpauseContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UnpauseFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> KillAsync(
        DriverContext context,
        string containerId,
        string signal = "SIGKILL",
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"kill --signal {QuoteArgumentIfNeeded(signal)} {QuoteArgumentIfNeeded(containerId)}";
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container kill failed",
              ErrorCodes.Container.KillFailed,
              CreateErrorContext(context, "KillContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.KillFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string containerId,
        bool force = false,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "rm";
        if (force)
          args += " -f";
        if (removeVolumes)
          args += " -v";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container remove failed",
              ErrorCodes.Container.RemoveFailed,
              CreateErrorContext(context, "RemoveContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RemoveFailed);
      }
    }

    #endregion
  }
}
