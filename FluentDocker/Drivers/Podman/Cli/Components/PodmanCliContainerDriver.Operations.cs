using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI container driver - execution, copy, and monitoring operations.
  /// </summary>
  public partial class PodmanCliContainerDriver
  {
    private static readonly char[] WhitespaceSeparators = [' ', '\t'];
    private static readonly string[] SlashSeparator = [" / "];

    #region Information Operations (continued)

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string containerId,
        bool follow = false, int? tail = null, bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        if (follow)
        {
          throw new NotSupportedException(
              "GetLogsAsync does not support follow=true because 'podman logs --follow' " +
              "streams indefinitely. Use IStreamDriver.StreamLogsAsync instead.");
        }

        var args = "logs";
        if (tail.HasValue)
          args += $" --tail {tail.Value}";
        if (timestamps)
          args += " --timestamps";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              result.Error ?? "Get logs failed", ErrorCodes.Container.LogsFailed,
              CreateErrorContext(context, "GetLogs", result), result.ExitCode);

        // podman logs writes to both stdout and stderr.
        // Combine both to capture all container output.
        var logs = !string.IsNullOrEmpty(result.Error)
            ? result.Output + result.Error
            : result.Output;
        return CommandResponse<string>.Ok(logs);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context, string containerId, string psOptions = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"top {QuoteArgumentIfNeeded(containerId)}";
        if (!string.IsNullOrEmpty(psOptions))
          args += $" {psOptions}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerProcesses>.Fail(
              result.Error ?? "Container top failed", ErrorCodes.Container.TopFailed,
              CreateErrorContext(context, "Top", result), result.ExitCode);

        var processes = ParseTopOutput(result.Output);
        return CommandResponse<ContainerProcesses>.Ok(processes);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerProcesses>.Fail(
            ex.Message, ErrorCodes.Container.TopFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"diff {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<FilesystemChange>>.Fail(
              result.Error ?? "Container diff failed", ErrorCodes.Container.DiffFailed,
              CreateErrorContext(context, "Diff", result), result.ExitCode);

        var changes = ParseDiffOutput(result.Output);
        return CommandResponse<IList<FilesystemChange>>.Ok(changes);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<FilesystemChange>>.Fail(
            ex.Message, ErrorCodes.Container.DiffFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerStatsResult>> StatsAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"stats --no-stream --format json {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerStatsResult>.Fail(
              result.Error ?? "Container stats failed", ErrorCodes.Container.StatsFailed,
              CreateErrorContext(context, "Stats", result), result.ExitCode);

        var stats = ParseStatsOutput(result.Output);
        return CommandResponse<ContainerStatsResult>.Ok(stats);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerStatsResult>.Fail(
            ex.Message, ErrorCodes.Container.StatsFailed);
      }
    }

    #endregion

    #region Execution Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ExecResult>> ExecAsync(
        DriverContext context, string containerId, ExecConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "exec";
        if (config.Detach)
          args += " -d";
        if (config.Tty)
          args += " -t";
        if (config.Interactive)
          args += " -i";
        if (config.Privileged)
          args += " --privileged";
        if (!string.IsNullOrEmpty(config.User))
          args += $" --user {QuoteArgumentIfNeeded(config.User)}";
        if (!string.IsNullOrEmpty(config.WorkingDir))
          args += $" -w {QuoteArgumentIfNeeded(config.WorkingDir)}";

        if (config.Environment != null)
          foreach (var env in config.Environment)
            args += $" -e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}";

        args += $" {QuoteArgumentIfNeeded(containerId)}";

        if (config.Command != null)
          foreach (var cmd in config.Command)
            args += $" {QuoteArgumentIfNeeded(cmd)}";

        // exec runs an arbitrary in-container command and can be long-lived; honor only
        // caller cancellation, not the buffered control-plane timeout.
        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);

        // Separate an INFRASTRUCTURE failure (podman could not run exec at all — no such
        // container, daemon error, process couldn't start) from the in-container command's
        // own legitimate non-zero exit, which must be reported as a successful exec carrying
        // that exit code (callers inspect ExecResult.ExitCode).
        if (IsExecInfrastructureFailure(result.ExitCode, result.Output, result.Error))
          return CommandResponse<ExecResult>.Fail(
              string.IsNullOrEmpty(result.Error) ? "Exec failed" : result.Error,
              ErrorCodes.Container.ExecFailed,
              CreateErrorContext(context, "Exec", result),
              result.ExitCode);

        return CommandResponse<ExecResult>.Ok(new ExecResult
        {
          ExitCode = result.ExitCode,
          StdOut = result.Output,
          StdErr = result.Error
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ExecResult>.Fail(
            ex.Message, ErrorCodes.Container.ExecFailed);
      }
    }

    /// <summary>
    /// Classifies a <c>podman exec</c> result as an infrastructure failure (podman itself
    /// could not run the exec) versus the in-container command merely exiting non-zero.
    /// <para>
    /// Returns <c>true</c> when the process-couldn't-start sentinel exit code (<c>-1</c>) is
    /// seen, when podman's "exec failure" convention exit code <c>125</c> is returned, or when
    /// there is no stdout and stderr carries a podman/daemon error marker. Bare phrases like
    /// "is not running" are deliberately NOT matched: they also appear in legitimate
    /// in-container tool output (systemctl/supervisord/health probes). Otherwise returns
    /// <c>false</c> so a real command's non-zero exit is preserved. Exit codes <c>126</c>
    /// (command found but not executable) and <c>127</c> (command not found) are conventions
    /// emitted by the in-container shell, not podman, so they are NOT treated as infra
    /// failures. Public so the heuristic can be unit-tested through the strong-named public
    /// surface (the driver itself spawns a real <c>podman</c> process).
    /// </para>
    /// </summary>
    /// <param name="exitCode">Exit code reported by command execution.</param>
    /// <param name="stdOut">Captured standard output.</param>
    /// <param name="stdErr">Captured standard error.</param>
    /// <returns><c>true</c> when the failure is infrastructure-level; otherwise <c>false</c>.</returns>
    public static bool IsExecInfrastructureFailure(int exitCode, string stdOut, string stdErr)
    {
      if (exitCode == -1)
        return true;

      // A command that produced stdout actually ran inside the container: its non-zero exit is
      // the command's own result, never a podman infrastructure error. This guard must precede
      // the 125 check below, since an in-container command may itself legitimately exit 125.
      if (!string.IsNullOrEmpty(stdOut))
        return false;

      // Podman returns 125 when the exec operation itself fails (e.g. no such container,
      // container not running) — this is podman's own failure code, not the command's.
      if (exitCode == 125)
        return true;

      // Backup heuristic for the rare non-125 infra failure with empty stdout. Markers are kept
      // specific to podman's own phrasing ("container is not running", "cannot connect to the
      // podman") so an in-container app emitting a generic "Error: cannot connect to redis" is
      // not misclassified. ponytail: these are still substrings, so exit code 125 above remains
      // the primary, unambiguous signal — extend with podman's exact error catalog if needed.
      var err = stdErr ?? string.Empty;
      return err.Contains("Error: ", StringComparison.OrdinalIgnoreCase)
          && (err.Contains("no such container", StringComparison.OrdinalIgnoreCase)
              || err.Contains("container is not running", StringComparison.OrdinalIgnoreCase)
              || err.Contains("unable to exec", StringComparison.OrdinalIgnoreCase)
              || err.Contains("cannot connect to the podman", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Copy Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyToAsync(
        DriverContext context, string containerId,
        string hostPath, string containerPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync(
            $"cp {QuoteArgumentIfNeeded(hostPath)} {QuoteArgumentIfNeeded($"{containerId}:{containerPath}")}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Copy to container failed", ErrorCodes.Container.CopyFailed,
              CreateErrorContext(context, "CopyTo", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyFromAsync(
        DriverContext context, string containerId,
        string containerPath, string hostPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync(
            $"cp {QuoteArgumentIfNeeded($"{containerId}:{containerPath}")} {QuoteArgumentIfNeeded(hostPath)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Copy from container failed", ErrorCodes.Container.CopyFailed,
              CreateErrorContext(context, "CopyFrom", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
      }
    }

    #endregion

    #region Export/Update Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ExportAsync(
        DriverContext context, string containerId, string outputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // export streams the whole container filesystem to disk — inherently long.
        var result = await ExecuteUnboundedCommandAsync(
            $"export -o {QuoteArgumentIfNeeded(outputPath)} {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container export failed", ErrorCodes.Container.ExportFailed,
              CreateErrorContext(context, "Export", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.ExportFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RenameAsync(
        DriverContext context, string containerId, string newName,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"rename {QuoteArgumentIfNeeded(containerId)} {QuoteArgumentIfNeeded(newName)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container rename failed", ErrorCodes.Container.RenameFailed,
              CreateErrorContext(context, "Rename", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RenameFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context, string containerId, ContainerUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"update";
        if (config.MemoryLimit.HasValue)
          args += $" --memory {config.MemoryLimit.Value}";
        if (config.MemorySwap.HasValue)
          args += $" --memory-swap {config.MemorySwap.Value}";
        if (config.MemoryReservation.HasValue)
          args += $" --memory-reservation {config.MemoryReservation.Value}";
        if (config.CpuShares.HasValue)
          args += $" --cpu-shares {config.CpuShares.Value}";
        if (config.CpuPeriod.HasValue)
          args += $" --cpu-period {config.CpuPeriod.Value}";
        if (config.CpuQuota.HasValue)
          args += $" --cpu-quota {config.CpuQuota.Value}";
        if (!string.IsNullOrEmpty(config.CpusetCpus))
          args += $" --cpuset-cpus {QuoteArgumentIfNeeded(config.CpusetCpus)}";
        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args += $" --restart {QuoteArgumentIfNeeded(config.RestartPolicy)}";
        if (config.PidsLimit.HasValue)
          args += $" --pids-limit {config.PidsLimit.Value}";

        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container update failed", ErrorCodes.Container.UpdateFailed,
              CreateErrorContext(context, "Update", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UpdateFailed);
      }
    }

    #endregion
  }
}
