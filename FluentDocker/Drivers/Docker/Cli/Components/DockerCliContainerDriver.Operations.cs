using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI container driver - run, wait, exec, copy, export, rename, and update operations.
  /// </summary>
  public partial class DockerCliContainerDriver
  {
    #region Run and Wait Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerRunResult>> RunAsync(
        DriverContext context,
        ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "run" };

        if (config.Detach)
          args.Add("-d");

        if (!string.IsNullOrEmpty(config.Name))
          args.Add($"--name {config.Name}");

        if (config.Environment != null)
          foreach (var env in config.Environment)
            args.Add($"-e {env.Key}={env.Value}");

        if (config.PortBindings != null)
          foreach (var port in config.PortBindings)
            args.Add($"-p {port.Value}:{port.Key}");

        if (config.Volumes != null)
          foreach (var volume in config.Volumes)
            args.Add($"-v {volume.Key}:{volume.Value}");

        if (!string.IsNullOrEmpty(config.NetworkMode))
          args.Add($"--network {config.NetworkMode}");

        // Static IPv4 address (requires custom network with subnet)
        if (!string.IsNullOrEmpty(config.Ipv4Address))
          args.Add($"--ip {config.Ipv4Address}");

        // Static IPv6 address (requires custom network with IPv6 enabled)
        if (!string.IsNullOrEmpty(config.Ipv6Address))
          args.Add($"--ip6 {config.Ipv6Address}");

        if (config.Labels != null)
          foreach (var label in config.Labels)
            args.Add($"--label {label.Key}={label.Value}");

        if (!string.IsNullOrEmpty(config.WorkingDirectory))
          args.Add($"-w {config.WorkingDirectory}");

        if (!string.IsNullOrEmpty(config.User))
          args.Add($"-u {config.User}");

        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args.Add($"--restart {config.RestartPolicy}");

        if (config.Privileged)
          args.Add("--privileged");

        if (config.AutoRemove)
          args.Add("--rm");

        // Links (legacy Docker feature)
        if (config.Links != null)
          foreach (var link in config.Links)
            args.Add($"--link {link}");

        if (config.Tty)
          args.Add("-t");

        if (config.Interactive)
          args.Add("-i");

        // Health check configuration
        if (config.HealthCheck != null)
        {
          if (config.HealthCheck.Test != null && config.HealthCheck.Test.Length > 0)
          {
            // Docker CLI --health-cmd expects just the command, without CMD-SHELL prefix
            // If the first element is "CMD-SHELL" or "CMD", skip it as it's only for Dockerfile format
            var testCommands = config.HealthCheck.Test;
            if (testCommands[0] == "CMD-SHELL" || testCommands[0] == "CMD")
              testCommands = testCommands.Skip(1).ToArray();

            var cmd = string.Join(" ", testCommands);
            args.Add($"--health-cmd \"{cmd}\"");
          }
          if (!string.IsNullOrEmpty(config.HealthCheck.Interval))
            args.Add($"--health-interval {config.HealthCheck.Interval}");
          if (!string.IsNullOrEmpty(config.HealthCheck.Timeout))
            args.Add($"--health-timeout {config.HealthCheck.Timeout}");
          if (config.HealthCheck.Retries > 0)
            args.Add($"--health-retries {config.HealthCheck.Retries}");
          if (!string.IsNullOrEmpty(config.HealthCheck.StartPeriod))
            args.Add($"--health-start-period {config.HealthCheck.StartPeriod}");
        }

        // Memory limit
        if (config.MemoryLimit.HasValue && config.MemoryLimit.Value > 0)
          args.Add($"--memory {config.MemoryLimit.Value}");

        // CPU shares
        if (config.CpuShares.HasValue && config.CpuShares.Value > 0)
          args.Add($"--cpu-shares {config.CpuShares.Value}");

        // Hostname
        if (!string.IsNullOrEmpty(config.Hostname))
          args.Add($"--hostname {config.Hostname}");

        // DNS servers
        if (config.Dns != null)
          foreach (var dns in config.Dns)
            args.Add($"--dns {dns}");

        // Extra hosts
        if (config.ExtraHosts != null)
          foreach (var host in config.ExtraHosts)
            args.Add($"--add-host {host.Key}:{host.Value}");

        // Entrypoint
        if (config.Entrypoint != null && config.Entrypoint.Length > 0)
          args.Add($"--entrypoint \"{string.Join(" ", config.Entrypoint)}\"");

        // Stop signal
        if (!string.IsNullOrEmpty(config.StopSignal))
          args.Add($"--stop-signal {config.StopSignal}");

        // Stop timeout
        if (config.StopTimeout.HasValue)
          args.Add($"--stop-timeout {config.StopTimeout.Value}");

        args.Add(config.Image);

        // Command - properly quote arguments that contain spaces or special characters
        if (config.Command != null && config.Command.Length > 0)
        {
          foreach (var cmdArg in config.Command)
          {
            args.Add(QuoteArgumentIfNeeded(cmdArg));
          }
        }

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ContainerRunResult>.Fail(
              result.Error ?? "Container run failed",
              ErrorCodes.Container.CreateFailed,
              CreateErrorContext(context, "RunContainer", result),
              result.ExitCode);
        }

        var runResult = new ContainerRunResult();

        if (config.Detach)
        {
          // When detached, output is the container ID
          runResult.Id = result.Output.Trim();
        }
        else
        {
          // When not detached, output is the container's stdout/stderr
          runResult.Output = result.Output;

          // Find the container ID by listing the most recently created container
          // Use the name if specified, otherwise get the last created container
          if (!string.IsNullOrEmpty(config.Name))
          {
            var listResult = await ExecuteCommandAsync($"ps -a --filter \"name={config.Name}\" --format \"{{{{.ID}}}}\" -n 1", cancellationToken);
            if (listResult.Success && !string.IsNullOrEmpty(listResult.Output))
            {
              runResult.Id = listResult.Output.Trim().Split('\n')[0];
            }
          }
          else
          {
            // Get the most recently created container
            var listResult = await ExecuteCommandAsync("ps -a --format \"{{.ID}}\" -n 1", cancellationToken);
            if (listResult.Success && !string.IsNullOrEmpty(listResult.Output))
            {
              runResult.Id = listResult.Output.Trim().Split('\n')[0];
            }
          }
        }

        return CommandResponse<ContainerRunResult>.Ok(runResult);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerRunResult>.Fail(ex.Message, ErrorCodes.Container.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerWaitResult>> WaitAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"wait {containerId}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ContainerWaitResult>.Fail(
              result.Error ?? "Container wait failed",
              ErrorCodes.Container.WaitFailed,
              CreateErrorContext(context, "WaitContainer", result),
              result.ExitCode);
        }

        int.TryParse(result.Output.Trim(), out var exitCode);

        return CommandResponse<ContainerWaitResult>.Ok(
            new ContainerWaitResult { ExitCode = exitCode });
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerWaitResult>.Fail(ex.Message, ErrorCodes.Container.WaitFailed);
      }
    }

    #endregion

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
            args.Add($"-e {env.Key}={env.Value}");

        args.Add(containerId);
        if (config.Command != null)
        {
          foreach (var cmdArg in config.Command)
          {
            args.Add(QuoteArgumentIfNeeded(cmdArg));
          }
        }

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

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
        var result = await ExecuteCommandAsync($"cp \"{hostPath}\" \"{containerId}:{containerPath}\"", cancellationToken);

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
        var result = await ExecuteCommandAsync($"cp \"{containerId}:{containerPath}\" \"{hostPath}\"", cancellationToken);

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
        var result = await ExecuteCommandAsync($"export -o \"{outputPath}\" {containerId}", cancellationToken);

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
        var result = await ExecuteCommandAsync($"rename {containerId} {newName}", cancellationToken);

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

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

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
