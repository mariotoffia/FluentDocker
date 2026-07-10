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
        if (StartsWithDash(config.Image))
          return FailInvalidLeadingDash<ContainerCreateResult>("Container image");

        var args = BuildCreateArgs("create", config);

        var result = await ExecuteUnboundedCommandAsync(context, string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerCreateResult>.Fail(
              ErrorOrDefault(result, "Container creation failed"),
              FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
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
            FailureCode(ex, ErrorCodes.Container.CreateFailed));
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
        var result = await ExecuteCommandAsync(context, $"start {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container start failed"),
              FailureCode(result.Error, ErrorCodes.Container.StartFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.StartFailed));
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
          args += $" -t {FormatInvariant(timeout.Value)}";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(
            context, args, DeriveGracefulStopCeiling(timeout, context), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container stop failed"),
              FailureCode(result.Error, ErrorCodes.Container.StopFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.StopFailed));
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
          args += $" -t {FormatInvariant(timeout.Value)}";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(
            context, args, DeriveGracefulStopCeiling(timeout, context), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container restart failed"),
              FailureCode(result.Error, ErrorCodes.Container.RestartFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.RestartFailed));
      }
    }

    // docker stop/restart send SIGTERM, wait -t seconds (default 10), then SIGKILL — intrinsically
    // bounded, so they must NOT use the unbounded path where a CancellationToken.None caller against a
    // wedged daemon hangs forever (DCLI-MAJ-5). Ceiling = timeout + 30s daemon-latency grace; a larger
    // caller RequestTimeout wins.
    private static TimeSpan DeriveGracefulStopCeiling(int? timeoutSeconds, DriverContext context)
    {
      var derived = TimeSpan.FromSeconds((timeoutSeconds ?? 10) + 30);
      return context?.RequestTimeout is { } rt && rt > derived ? rt : derived;
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PauseAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"pause {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container pause failed"),
              FailureCode(result.Error, ErrorCodes.Container.PauseFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.PauseFailed));
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
        var result = await ExecuteCommandAsync(context, $"unpause {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container unpause failed"),
              FailureCode(result.Error, ErrorCodes.Container.UnpauseFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.UnpauseFailed));
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
        var args = $"kill --signal {QuotePositionalArgument(signal ?? "SIGKILL", nameof(signal))} {QuotePositionalArgument(containerId, nameof(containerId))}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container kill failed"),
              FailureCode(result.Error, ErrorCodes.Container.KillFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.KillFailed));
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
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container remove failed"),
              FailureCode(result.Error, ErrorCodes.Container.RemoveFailed),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.RemoveFailed));
      }
    }

    #endregion
  }
}
