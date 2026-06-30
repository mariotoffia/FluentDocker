using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI container driver - exec, copy, export, rename, and update operations.
  /// </summary>
  public partial class DockerCliContainerDriver
  {
    #region Execution Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ExecResult>> ExecAsync(
        DriverContext context,
        string containerId,
        ExecConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "exec" };

        if (config.Detach)
          args.Add("-d");
        if (config.Interactive)
          args.Add("-i");
        if (config.Tty)
          args.Add("-t");
        if (config.Privileged)
          args.Add("--privileged");
        if (!string.IsNullOrEmpty(config.User))
          args.Add($"-u {QuoteArgumentIfNeeded(config.User)}");
        if (!string.IsNullOrEmpty(config.WorkingDir))
          args.Add($"-w {QuoteArgumentIfNeeded(config.WorkingDir)}");
        if (config.Environment != null)
          foreach (var env in config.Environment)
            args.Add($"-e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}");

        args.Add(QuoteArgumentIfNeeded(containerId));
        if (config.Command != null)
        {
          foreach (var cmdArg in config.Command)
          {
            args.Add(QuoteArgumentIfNeeded(cmdArg));
          }
        }

        var result = await ExecuteUnboundedCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        // Separate an INFRASTRUCTURE failure (docker could not run exec at all — no such
        // container, daemon error, process couldn't start) from the in-container command's
        // own legitimate non-zero exit, which must be reported as a successful exec carrying
        // that exit code (callers inspect ExecResult.ExitCode).
        if (IsExecInfrastructureFailure(result.ExitCode, result.Output, result.Error))
        {
          return CommandResponse<ExecResult>.Fail(
              string.IsNullOrEmpty(result.Error) ? "Exec failed" : result.Error,
              ErrorCodes.Container.ExecFailed,
              CreateErrorContext(context, "Exec", result),
              result.ExitCode);
        }

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
        return CommandResponse<ExecResult>.Fail(ex.Message, ErrorCodes.Container.ExecFailed);
      }
    }

    /// <summary>
    /// Classifies a <c>docker exec</c> result as an infrastructure failure (docker itself
    /// could not run the exec) versus the in-container command merely exiting non-zero.
    /// <para>
    /// Returns <c>true</c> when the process-couldn't-start sentinel exit code (<c>-1</c>) is
    /// seen, or when there is no stdout and stderr carries a docker-CLI/daemon error marker
    /// (<c>Error response from daemon</c> / <c>Cannot connect to the Docker daemon</c>). Bare
    /// substrings like "is not running" are deliberately NOT matched: they also appear in
    /// legitimate in-container tool output (systemctl/supervisord/health probes). Otherwise
    /// returns <c>false</c> so a real command's non-zero exit is preserved rather than
    /// reported as a false failure. Public so the heuristic can be unit-tested through the
    /// strong-named public surface (the driver itself spawns a real <c>docker</c> process).
    /// </para>
    /// <para>
    /// On the infra-failure path the exit code is surfaced on <c>CommandResponse.ExitCode</c>
    /// (with <c>Data</c> null); on success it is on <c>Data.ExitCode</c> of the returned
    /// <see cref="ExecResult"/>.
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

      if (!string.IsNullOrEmpty(stdOut))
        return false;

      var err = stdErr ?? string.Empty;
      return err.Contains("Error response from daemon", StringComparison.OrdinalIgnoreCase)
          || err.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Copy Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyToAsync(
        DriverContext context,
        string containerId,
        string hostPath,
        string containerPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync($"cp \"{hostPath}\" \"{containerId}:{containerPath}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Copy to container failed",
              ErrorCodes.Container.CopyFailed,
              CreateErrorContext(context, "CopyToContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> CopyFromAsync(
        DriverContext context,
        string containerId,
        string containerPath,
        string hostPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync($"cp \"{containerId}:{containerPath}\" \"{hostPath}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Copy from container failed",
              ErrorCodes.Container.CopyFailed,
              CreateErrorContext(context, "CopyFromContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
      }
    }

    #endregion

    #region Export/Rename/Update Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ExportAsync(
        DriverContext context,
        string containerId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync($"export -o \"{outputPath}\" {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container export failed",
              ErrorCodes.Container.ExportFailed,
              CreateErrorContext(context, "ExportContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.ExportFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RenameAsync(
        DriverContext context,
        string containerId,
        string newName,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"rename {QuoteArgumentIfNeeded(containerId)} {QuoteArgumentIfNeeded(newName)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container rename failed",
              ErrorCodes.Container.RenameFailed,
              CreateErrorContext(context, "RenameContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RenameFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context,
        string containerId,
        ContainerUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "update" };

        if (config.MemoryLimit.HasValue)
          args.Add($"--memory {config.MemoryLimit.Value}");
        if (config.MemorySwap.HasValue)
          args.Add($"--memory-swap {config.MemorySwap.Value}");
        if (config.MemoryReservation.HasValue)
          args.Add($"--memory-reservation {config.MemoryReservation.Value}");
        if (config.CpuShares.HasValue)
          args.Add($"--cpu-shares {config.CpuShares.Value}");
        if (config.CpuPeriod.HasValue)
          args.Add($"--cpu-period {config.CpuPeriod.Value}");
        if (config.CpuQuota.HasValue)
          args.Add($"--cpu-quota {config.CpuQuota.Value}");
        if (!string.IsNullOrEmpty(config.CpusetCpus))
          args.Add($"--cpuset-cpus {QuoteArgumentIfNeeded(config.CpusetCpus)}");
        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args.Add($"--restart {QuoteArgumentIfNeeded(config.RestartPolicy)}");
        if (config.PidsLimit.HasValue)
          args.Add($"--pids-limit {config.PidsLimit.Value}");

        args.Add(QuoteArgumentIfNeeded(containerId));

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container update failed",
              ErrorCodes.Container.UpdateFailed,
              CreateErrorContext(context, "UpdateContainer", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UpdateFailed);
      }
    }

    #endregion
  }
}
