using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI compose driver: create operations and exec-failure classification.
  /// Split from <c>DockerCliComposeDriver.Info.cs</c> to keep partial files under the line-count limit.
  /// </summary>
  public partial class DockerCliComposeDriver
  {
    #region Create Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CreateAsync(
        DriverContext context,
        ComposeCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildComposeArgs(config) + " " + BuildCreateSubArgs(config);
        if (config.Services.Count > 0)
          args += " " + QuoteServices(config.Services);

        var result = await ExecuteUnboundedCommandAsync(context, args, config.Environment, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                ErrorOrDefault(result, "Compose create failed"), FailureCode(result.Error, ErrorCodes.Compose.CreateFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Compose.CreateFailed));
      }
    }

    #endregion

    /// <summary>
    /// Compose-exec variant of <see cref="DockerCliContainerDriver.IsExecInfrastructureFailure"/>:
    /// classifies whether a <c>docker compose exec</c> failure was infrastructure-level
    /// (e.g. daemon unreachable) rather than the executed command itself exiting non-zero.
    /// </summary>
    /// <param name="exitCode">Exit code reported by command execution.</param>
    /// <param name="stdOut">Captured standard output.</param>
    /// <param name="stdErr">Captured standard error.</param>
    /// <returns><c>true</c> when the failure is infrastructure-level; otherwise <c>false</c>.</returns>
    public static bool IsComposeExecInfrastructureFailure(int exitCode, string stdOut, string stdErr) =>
        DockerCliContainerDriver.IsExecInfrastructureFailure(exitCode, stdOut, stdErr);
  }
}
