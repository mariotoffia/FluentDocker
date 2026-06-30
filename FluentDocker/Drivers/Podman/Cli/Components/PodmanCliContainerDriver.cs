using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of IContainerDriver.
  /// Core lifecycle and information operations.
  /// </summary>
  public partial class PodmanCliContainerDriver(IPodmanBinaryResolver binaryResolver) : PodmanCliDriverBase(binaryResolver), IContainerDriver
  {

    #region Lifecycle Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context, ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildCreateArgs("create", config);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerCreateResult>.Fail(
              result.Error ?? "Container create failed", ErrorCodes.Container.CreateFailed,
              CreateErrorContext(context, "CreateContainer", result), result.ExitCode);

        return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult
        {
          Id = result.Output?.Trim(),
          Name = config.Name
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerCreateResult>.Fail(
            ex.Message, ErrorCodes.Container.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerRunResult>> RunAsync(
        DriverContext context, ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildCreateArgs("run", config, config.Detach);

        // run can be inherently long (it waits for a non-detached container to finish);
        // honor only caller cancellation, not the buffered control-plane timeout.
        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerRunResult>.Fail(
              result.Error ?? "Container run failed", ErrorCodes.Container.CreateFailed,
              CreateErrorContext(context, "RunContainer", result), result.ExitCode);

        return CommandResponse<ContainerRunResult>.Ok(new ContainerRunResult
        {
          Id = config.Detach ? result.Output?.Trim() : null,
          Output = config.Detach ? null : result.Output
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerRunResult>.Fail(
            ex.Message, ErrorCodes.Container.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"start {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container start failed", ErrorCodes.Container.StartFailed,
              CreateErrorContext(context, "StartContainer", result), result.ExitCode);

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
        DriverContext context, string containerId, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "stop";
        if (timeout.HasValue)
          args += $" -t {timeout.Value}";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        // stop waits up to the grace period for the container to exit — inherently long.
        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container stop failed", ErrorCodes.Container.StopFailed,
              CreateErrorContext(context, "StopContainer", result), result.ExitCode);

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
        DriverContext context, string containerId, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "restart";
        if (timeout.HasValue)
          args += $" -t {timeout.Value}";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        // restart waits up to the grace period for the container to stop — inherently long.
        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container restart failed", ErrorCodes.Container.RestartFailed,
              CreateErrorContext(context, "RestartContainer", result), result.ExitCode);

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
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"pause {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container pause failed", ErrorCodes.Container.PauseFailed,
              CreateErrorContext(context, "PauseContainer", result), result.ExitCode);

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
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"unpause {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container unpause failed", ErrorCodes.Container.UnpauseFailed,
              CreateErrorContext(context, "UnpauseContainer", result), result.ExitCode);

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
        DriverContext context, string containerId, string signal = "SIGKILL",
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"kill --signal {QuoteArgumentIfNeeded(signal)} {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container kill failed", ErrorCodes.Container.KillFailed,
              CreateErrorContext(context, "KillContainer", result), result.ExitCode);

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
        DriverContext context, string containerId,
        bool force = false, bool removeVolumes = false,
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
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container remove failed", ErrorCodes.Container.RemoveFailed,
              CreateErrorContext(context, "RemoveContainer", result), result.ExitCode);

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

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerWaitResult>> WaitAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // wait blocks until the container exits — inherently long; honor only caller cancellation.
        var result = await ExecuteUnboundedCommandAsync($"wait {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerWaitResult>.Fail(
              result.Error ?? "Container wait failed", ErrorCodes.Container.WaitFailed,
              CreateErrorContext(context, "WaitContainer", result), result.ExitCode);

        _ = int.TryParse(result.Output?.Trim(), out var exitCode);
        return CommandResponse<ContainerWaitResult>.Ok(new ContainerWaitResult
        {
          ExitCode = exitCode
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerWaitResult>.Fail(
            ex.Message, ErrorCodes.Container.WaitFailed);
      }
    }

    #endregion

    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Container>> InspectAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"inspect {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Container>.Fail(
              result.Error ?? "Container inspect failed", ErrorCodes.Container.InspectFailed,
              CreateErrorContext(context, "InspectContainer", result), result.ExitCode);

        var container = ParseContainerInspect(result.Output);
        return CommandResponse<Container>.Ok(container);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Container>.Fail(
            ex.Message, ErrorCodes.Container.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context, ContainerListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildListArgs(filter);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<Container>>.Fail(
              result.Error ?? "Container list failed", ErrorCodes.General.Unknown,
              CreateErrorContext(context, "ListContainers", result), result.ExitCode);

        var containers = ParseContainerList(result.Output);
        return CommandResponse<IList<Container>>.Ok(containers);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Container>>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    #endregion
  }
}
