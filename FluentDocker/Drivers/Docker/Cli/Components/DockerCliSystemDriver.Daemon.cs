using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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
    #region Daemon Operations (Docker Desktop specific)

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync("-SwitchDaemon", cancellationToken).ConfigureAwait(false);
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync("-SwitchLinuxEngine", cancellationToken).ConfigureAwait(false);
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteDockerCliCommandAsync("-SwitchWindowsEngine", cancellationToken).ConfigureAwait(false);
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Executes a Docker CLI command (Docker Desktop specific).
    /// </summary>
    private async Task<SimpleCommandResult> ExecuteDockerCliCommandAsync(string arguments, CancellationToken cancellationToken)
    {
      try
      {
        Process process = null;
        try
        {
          process = new Process
          {
            StartInfo = new ProcessStartInfo
            {
              FileName = BinaryResolver?.ResolveBinaryPath("dockercli") ?? "dockercli",
              Arguments = arguments,
              RedirectStandardOutput = true,
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
            }
          };

          var output = new StringBuilder();
          var error = new StringBuilder();

          process.OutputDataReceived += (s, e) =>
          {
            if (!string.IsNullOrEmpty(e.Data))
              output.AppendLine(e.Data);
          };

          process.ErrorDataReceived += (s, e) =>
          {
            if (!string.IsNullOrEmpty(e.Data))
              error.AppendLine(e.Data);
          };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();

          await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

          // Parameterless overload flushes the async OutputDataReceived/
          // ErrorDataReceived handlers to EOF; the timed overload does not.
          process.WaitForExit();

          return new SimpleCommandResult
          {
            Success = process.ExitCode == 0,
            Output = output.ToString(),
            Error = error.ToString(),
            ExitCode = process.ExitCode
          };
        }
        catch (OperationCanceledException)
        {
          try
          {
            if (process is { HasExited: false })
              process.Kill(entireProcessTree: true);
          }
          catch
          {
            // best effort: process may have exited between the check and kill
          }
          throw;
        }
        finally
        {
          process?.Dispose();
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return new SimpleCommandResult
        {
          Success = false,
          Error = ex.Message,
          ExitCode = -1
        };
      }
    }

    #endregion
  }
}
