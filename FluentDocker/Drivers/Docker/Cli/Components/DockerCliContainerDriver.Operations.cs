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
          args.Add($"-u {config.User}");
        if (!string.IsNullOrEmpty(config.WorkingDir))
          args.Add($"-w {config.WorkingDir}");
        if (config.Environment != null)
          foreach (var env in config.Environment)
            args.Add($"-e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}");

        args.Add(containerId);
        if (config.Command != null)
        {
          foreach (var cmdArg in config.Command)
          {
            args.Add(QuoteArgumentIfNeeded(cmdArg));
          }
        }

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        return CommandResponse<ExecResult>.Ok(new ExecResult
        {
          ExitCode = result.ExitCode,
          StdOut = result.Output,
          StdErr = result.Error
        });
      }
      catch (Exception ex)
      {
        return CommandResponse<ExecResult>.Fail(ex.Message, ErrorCodes.Container.ExecFailed);
      }
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
        var result = await ExecuteCommandAsync($"cp \"{hostPath}\" \"{containerId}:{containerPath}\"", cancellationToken).ConfigureAwait(false);

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
        var result = await ExecuteCommandAsync($"cp \"{containerId}:{containerPath}\" \"{hostPath}\"", cancellationToken).ConfigureAwait(false);

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
        var result = await ExecuteCommandAsync($"export -o \"{outputPath}\" {containerId}", cancellationToken).ConfigureAwait(false);

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
        var result = await ExecuteCommandAsync($"rename {containerId} {newName}", cancellationToken).ConfigureAwait(false);

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
          args.Add($"--cpuset-cpus {config.CpusetCpus}");
        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args.Add($"--restart {config.RestartPolicy}");
        if (config.PidsLimit.HasValue)
          args.Add($"--pids-limit {config.PidsLimit.Value}");

        args.Add(containerId);

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
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UpdateFailed);
      }
    }

    #endregion
  }
}
