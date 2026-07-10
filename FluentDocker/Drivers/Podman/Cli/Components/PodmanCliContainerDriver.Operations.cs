using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    /// <summary>Gets bounded, non-following container logs using <c>podman logs</c>.</summary>
    /// <remarks>
    /// Podman process stdout and stderr are retained as separate 256 KiB rolling tails and then
    /// merged with stderr appended after stdout, so chronological interleaving can be lost. Use
    /// <see cref="FluentDocker.Drivers.IStreamDriver.StreamLogsAsync"/> when stdout/stderr order matters.
    /// </remarks>
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string containerId,
        bool follow = false, int? tail = null, bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
      // Guarded ABOVE the try so the deterministic misuse surfaces the honest, Docker-parity code
      // (Container.LogsFailed) instead of being relabeled General.Unknown by the catch-all (PDM-MAJ-3).
      if (follow)
      {
        return CommandResponse<string>.Fail(
            "GetLogsAsync does not support follow=true because 'podman logs --follow' " +
            "streams indefinitely. Use IStreamDriver.StreamLogsAsync instead.",
            ErrorCodes.Container.LogsFailed);
      }

      try
      {
        var args = "logs";
        if (tail.HasValue)
          args += $" --tail {tail.Value}";
        if (timestamps)
          args += " --timestamps";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Get logs failed"), FailureCode(result.Error, ErrorCodes.Container.LogsFailed),
              CreateErrorContext(context, "GetLogs", result), result.ExitCode);

        // podman logs writes to both stdout and stderr.
        // Combine both to capture all container output.
        return CommandResponse<string>.Ok(MergeOutputAndError(result.Output, result.Error));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context, string containerId, string psOptions = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"top {QuotePositionalArgument(containerId, nameof(containerId))}";
        if (!string.IsNullOrEmpty(psOptions))
          args += " " + string.Join(" ", psOptions.Split(
              WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries).Select(QuoteArgumentIfNeeded));

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerProcesses>.Fail(
              ErrorOrDefault(result, "Container top failed"), FailureCode(result.Error, ErrorCodes.Container.TopFailed),
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
            ex.Message, FailureCode(ex, ErrorCodes.Container.TopFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"diff {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<FilesystemChange>>.Fail(
              ErrorOrDefault(result, "Container diff failed"), FailureCode(result.Error, ErrorCodes.Container.DiffFailed),
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
            ex.Message, FailureCode(ex, ErrorCodes.Container.DiffFailed));
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
            context, $"stats --no-stream --format json {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerStatsResult>.Fail(
              ErrorOrDefault(result, "Container stats failed"), FailureCode(result.Error, ErrorCodes.Container.StatsFailed),
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
            ex.Message, FailureCode(ex, ErrorCodes.Container.StatsFailed));
      }
    }

    #endregion

    #region Execution Operations

    /// <summary>Executes a command inside a container using <c>podman exec</c>.</summary>
    /// <remarks>
    /// Captured stdout and stderr retain only the final 256 KiB tail for long-running execs.
    /// Podman uses exit code <c>125</c> when the exec operation itself fails. This driver
    /// treats <c>125</c> with empty stdout and a Podman error marker as infrastructure failure.
    /// Other non-zero exits are preserved in <see cref="ExecResult.ExitCode"/>.
    /// </remarks>
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

        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        if (config.Command != null)
          foreach (var cmd in config.Command)
            args += $" {QuoteArgumentIfNeeded(cmd)}";

        // exec runs an arbitrary in-container command and can be long-lived; honor only
        // caller cancellation, not the buffered control-plane timeout.
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

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
            ex.Message, FailureCode(ex, ErrorCodes.Container.ExecFailed));
      }
    }

    /// <summary>
    /// Classifies a <c>podman exec</c> result as an infrastructure failure (podman itself
    /// could not run the exec) versus the in-container command merely exiting non-zero.
    /// <para>
    /// Returns <c>true</c> when the process-couldn't-start sentinel exit code (<c>-1</c>) is
    /// seen, when podman's exec-failed exit code <c>125</c> has no stdout, or when there is no
    /// stdout and stderr carries a podman/daemon error marker. Bare phrases like
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

      if (exitCode == 0)
        return false;

      // A command that produced stdout actually ran inside the container: its non-zero exit is
      // the command's own result, never a podman infrastructure error. This guard must precede
      // the 125 check below, since an in-container command may itself legitimately exit 125.
      if (!string.IsNullOrEmpty(stdOut))
        return false;

      if (exitCode == 125)
        return true;

      // Infra heuristic for failures with empty stdout. Markers are kept
      // specific to podman's own phrasing ("container is not running", "Cannot connect to Podman")
      // so an in-container app emitting a generic "Error: cannot connect to redis" is
      // not misclassified. ponytail: these are still substrings; extend with podman's exact
      // error catalog if needed.
      var err = stdErr ?? string.Empty;
      var hasPodmanMarker = err.Contains("Error: ", StringComparison.OrdinalIgnoreCase)
          && (err.Contains("no such container", StringComparison.OrdinalIgnoreCase)
              || err.Contains("no container with name", StringComparison.OrdinalIgnoreCase)
              || err.Contains("container is not running", StringComparison.OrdinalIgnoreCase)
              || err.Contains("unable to exec", StringComparison.OrdinalIgnoreCase)
              || err.Contains("Cannot connect to Podman", StringComparison.Ordinal));
      return hasPodmanMarker;
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
        QuotePositionalArgument(containerId, nameof(containerId));
        QuotePositionalArgument(hostPath, nameof(hostPath));
        var result = await ExecuteUnboundedCommandAsync(
            context, $"cp {QuoteArgumentIfNeeded(hostPath)} {QuoteArgumentIfNeeded($"{containerId}:{containerPath}")}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Copy to container failed"), FailureCode(result.Error, ErrorCodes.Container.CopyFailed),
              CreateErrorContext(context, "CopyTo", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.CopyFailed));
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
        QuotePositionalArgument(containerId, nameof(containerId));
        QuotePositionalArgument(hostPath, nameof(hostPath));
        var result = await ExecuteUnboundedCommandAsync(
            context, $"cp {QuoteArgumentIfNeeded($"{containerId}:{containerPath}")} {QuoteArgumentIfNeeded(hostPath)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Copy from container failed"), FailureCode(result.Error, ErrorCodes.Container.CopyFailed),
              CreateErrorContext(context, "CopyFrom", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.CopyFailed));
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
            context, $"export -o {QuoteArgumentIfNeeded(outputPath)} {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container export failed"), FailureCode(result.Error, ErrorCodes.Container.ExportFailed),
              CreateErrorContext(context, "Export", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.ExportFailed));
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
            context, $"rename {QuotePositionalArgument(containerId, nameof(containerId))} {QuotePositionalArgument(newName, nameof(newName))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container rename failed"), FailureCode(result.Error, ErrorCodes.Container.RenameFailed),
              CreateErrorContext(context, "Rename", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.RenameFailed));
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

        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container update failed"), FailureCode(result.Error, ErrorCodes.Container.UpdateFailed),
              CreateErrorContext(context, "Update", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.UpdateFailed));
      }
    }

    #endregion
  }
}
