#nullable disable warnings
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker Desktop specific daemon/engine switch operations of
  /// <see cref="DockerCliSystemDriver"/>. Split into its own partial file purely to keep
  /// each source file within the repository's 500-line limit.
  /// </summary>
  public partial class DockerCliSystemDriver
  {
    // Appended to Switch* exception failures: the dominant cause is the dockercli binary being
    // absent (it only ships with Docker Desktop for Windows), which surfaces here as a resolver
    // or process-start exception.
    private const string DockerCliRequirement =
        " Engine switching requires Docker Desktop's dockercli, which is only available with Docker Desktop on Windows.";

    #region Daemon Operations (Docker Desktop specific)

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync(context, "-SwitchDaemon", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Switch daemon failed"), FailureCode(result.Error, ErrorCodes.General.Unknown));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message + DockerCliRequirement, FailureCode(ex, ErrorCodes.Driver.NotAvailable));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync(context, "-SwitchLinuxEngine", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Switch to Linux failed"), FailureCode(result.Error, ErrorCodes.General.Unknown));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message + DockerCliRequirement, FailureCode(ex, ErrorCodes.Driver.NotAvailable));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync(context, "-SwitchWindowsEngine", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Switch to Windows failed"), FailureCode(result.Error, ErrorCodes.General.Unknown));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message + DockerCliRequirement, FailureCode(ex, ErrorCodes.Driver.NotAvailable));
      }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Executes a Docker CLI command (Docker Desktop specific).
    /// </summary>
    private async Task<SimpleCommandResult> ExecuteDockerCliCommandAsync(
        DriverContext context,
        string arguments,
        CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      // ponytail: engine switch is bounded by the shared RequestTimeout (5-min default when unset);
      // give Switch* its own longer ceiling if a tuned-down RequestTimeout starts aborting slow switches.
      return await ExecuteProcessAsync(
          BinaryResolver?.ResolveBinaryPath("dockercli") ?? "dockercli",
          arguments,
          null,
          null,
          SudoMechanism.None,
          null,
          ResolveBufferedTimeout(effectiveContext),
          cancellationToken).ConfigureAwait(false);
    }

    #endregion
  }
}
