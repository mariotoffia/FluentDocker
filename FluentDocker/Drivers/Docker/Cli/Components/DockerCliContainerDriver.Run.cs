using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI container driver - run and wait operations.
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
      // Use --cidfile for race-free container ID discovery in non-detached mode.
      string cidFile = null;
      try
      {
        var args = new List<string> { "run" };

        if (config.Detach)
          args.Add("-d");

        if (!config.Detach)
        {
          cidFile = Path.Combine(Path.GetTempPath(), $"docker-cid-{Guid.NewGuid():N}");
          args.Add($"--cidfile {QuoteArgumentIfNeeded(cidFile)}");
        }

        if (!string.IsNullOrEmpty(config.Name))
          args.Add($"--name {QuoteArgumentIfNeeded(config.Name)}");

        if (config.Environment != null)
          foreach (var env in config.Environment)
            args.Add($"-e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}");

        if (config.PortBindings != null)
          foreach (var port in config.PortBindings)
            args.Add($"-p {QuoteArgumentIfNeeded($"{port.Value}:{port.Key}")}");

        if (config.Volumes != null)
          foreach (var volume in config.Volumes)
            args.Add($"-v {QuoteArgumentIfNeeded($"{volume.Key}:{volume.Value}")}");

        if (!string.IsNullOrEmpty(config.NetworkMode))
          args.Add($"--network {QuoteArgumentIfNeeded(config.NetworkMode)}");

        // Static IPv4 address (requires custom network with subnet)
        if (!string.IsNullOrEmpty(config.Ipv4Address))
          args.Add($"--ip {QuoteArgumentIfNeeded(config.Ipv4Address)}");

        // Static IPv6 address (requires custom network with IPv6 enabled)
        if (!string.IsNullOrEmpty(config.Ipv6Address))
          args.Add($"--ip6 {QuoteArgumentIfNeeded(config.Ipv6Address)}");

        if (config.Labels != null)
          foreach (var label in config.Labels)
            args.Add($"--label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}");

        if (!string.IsNullOrEmpty(config.WorkingDirectory))
          args.Add($"-w {QuoteArgumentIfNeeded(config.WorkingDirectory)}");

        if (!string.IsNullOrEmpty(config.User))
          args.Add($"-u {QuoteArgumentIfNeeded(config.User)}");

        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args.Add($"--restart {QuoteArgumentIfNeeded(config.RestartPolicy)}");

        if (config.Privileged)
          args.Add("--privileged");

        if (config.AutoRemove)
          args.Add("--rm");

        // Links (legacy Docker feature)
        if (config.Links != null)
          foreach (var link in config.Links)
            args.Add($"--link {QuoteArgumentIfNeeded(link)}");

        // Network aliases
        if (config.NetworkAliases != null)
          foreach (var networkAlias in config.NetworkAliases)
            foreach (var alias in networkAlias.Value)
              args.Add($"--network-alias {QuoteArgumentIfNeeded(alias)}");

        // Security & advanced
        if (config.ReadonlyRootfs)
          args.Add("--read-only");
        if (config.ShmSize.HasValue)
          args.Add($"--shm-size {config.ShmSize.Value}");
        if (!string.IsNullOrEmpty(config.Platform))
          args.Add($"--platform {QuoteArgumentIfNeeded(config.Platform)}");
        if (!string.IsNullOrEmpty(config.Runtime))
          args.Add($"--runtime {QuoteArgumentIfNeeded(config.Runtime)}");
        if (config.CapAdd != null)
          foreach (var cap in config.CapAdd)
            args.Add($"--cap-add {QuoteArgumentIfNeeded(cap)}");
        if (config.CapDrop != null)
          foreach (var cap in config.CapDrop)
            args.Add($"--cap-drop {QuoteArgumentIfNeeded(cap)}");
        if (config.SecurityOpt != null)
          foreach (var opt in config.SecurityOpt)
            args.Add($"--security-opt {QuoteArgumentIfNeeded(opt)}");
        if (config.Tmpfs != null)
          foreach (var tmpfs in config.Tmpfs)
            args.Add(string.IsNullOrEmpty(tmpfs.Value)
                ? $"--tmpfs {QuoteArgumentIfNeeded(tmpfs.Key)}" : $"--tmpfs {QuoteArgumentIfNeeded($"{tmpfs.Key}:{tmpfs.Value}")}");
        if (config.Devices != null)
          foreach (var dev in config.Devices)
            args.Add(dev.Key == dev.Value
                ? $"--device {QuoteArgumentIfNeeded(dev.Key)}" : $"--device {QuoteArgumentIfNeeded($"{dev.Key}:{dev.Value}")}");

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
          args.Add($"--hostname {QuoteArgumentIfNeeded(config.Hostname)}");

        // DNS servers
        if (config.Dns != null)
          foreach (var dns in config.Dns)
            args.Add($"--dns {QuoteArgumentIfNeeded(dns)}");

        // Extra hosts
        if (config.ExtraHosts != null)
          foreach (var host in config.ExtraHosts)
            args.Add($"--add-host {QuoteArgumentIfNeeded($"{host.Key}:{host.Value}")}");

        // Entrypoint — Docker CLI --entrypoint only accepts the executable.
        // Additional arguments from the entrypoint array are prepended to Command.
        string[] entrypointArgs = null;
        if (config.Entrypoint != null && config.Entrypoint.Length > 0)
        {
          args.Add($"--entrypoint {QuoteArgumentIfNeeded(config.Entrypoint[0])}");
          if (config.Entrypoint.Length > 1)
            entrypointArgs = config.Entrypoint[1..];
        }

        // Stop signal
        if (!string.IsNullOrEmpty(config.StopSignal))
          args.Add($"--stop-signal {config.StopSignal}");

        // Stop timeout
        if (config.StopTimeout.HasValue)
          args.Add($"--stop-timeout {config.StopTimeout.Value}");

        args.Add(config.Image);

        // Entrypoint args (overflow from --entrypoint) come before Command
        if (entrypointArgs != null)
        {
          foreach (var epArg in entrypointArgs)
            args.Add(QuoteArgumentIfNeeded(epArg));
        }

        // Command - properly quote arguments that contain spaces or special characters
        if (config.Command != null && config.Command.Length > 0)
        {
          foreach (var cmdArg in config.Command)
          {
            args.Add(QuoteArgumentIfNeeded(cmdArg));
          }
        }

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

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

          // Read container ID from --cidfile (race-free, set earlier in args).
          if (cidFile != null && File.Exists(cidFile))
          {
            runResult.Id = (await File.ReadAllTextAsync(cidFile, cancellationToken)).Trim();
          }
        }

        return CommandResponse<ContainerRunResult>.Ok(runResult);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerRunResult>.Fail(ex.Message, ErrorCodes.Container.CreateFailed);
      }
      finally
      {
        if (cidFile != null && File.Exists(cidFile))
        {
          try
          { File.Delete(cidFile); }
          catch { /* best effort cleanup */ }
        }
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
        var result = await ExecuteCommandAsync($"wait {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerWaitResult>.Fail(
              result.Error ?? "Container wait failed",
              ErrorCodes.Container.WaitFailed,
              CreateErrorContext(context, "WaitContainer", result),
              result.ExitCode);
        }

        _ = int.TryParse(result.Output.Trim(), out var exitCode);

        return CommandResponse<ContainerWaitResult>.Ok(
            new ContainerWaitResult { ExitCode = exitCode });
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerWaitResult>.Fail(ex.Message, ErrorCodes.Container.WaitFailed);
      }
    }

    #endregion
  }
}
