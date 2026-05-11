using System;
using System.Collections.Generic;
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

        var args = new List<string> { "create" };

        // Name
        if (!string.IsNullOrEmpty(config.Name))
          args.Add($"--name {QuoteArgumentIfNeeded(config.Name)}");

        // Environment variables
        if (config.Environment != null)
        {
          foreach (var env in config.Environment)
            args.Add($"-e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}");
        }

        // Port bindings: PortBindings dict is Key=containerPort, Value=hostPort.
        // Docker -p syntax is hostPort:containerPort, so we emit Value:Key.
        if (config.PortBindings != null)
        {
          foreach (var port in config.PortBindings)
            args.Add($"-p {QuoteArgumentIfNeeded($"{port.Value}:{port.Key}")}");
        }

        // Volume mounts (host:container)
        if (config.Volumes != null)
        {
          foreach (var volume in config.Volumes)
            args.Add($"-v {QuoteArgumentIfNeeded($"{volume.Key}:{volume.Value}")}");
        }

        // Network mode
        if (!string.IsNullOrEmpty(config.NetworkMode))
          args.Add($"--network {QuoteArgumentIfNeeded(config.NetworkMode)}");

        // Networks
        if (config.Networks != null)
        {
          foreach (var network in config.Networks)
            args.Add($"--network {QuoteArgumentIfNeeded(network)}");
        }

        // Labels
        if (config.Labels != null)
        {
          foreach (var label in config.Labels)
            args.Add($"--label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}");
        }

        // Working directory
        if (!string.IsNullOrEmpty(config.WorkingDirectory))
          args.Add($"-w {QuoteArgumentIfNeeded(config.WorkingDirectory)}");

        // User
        if (!string.IsNullOrEmpty(config.User))
          args.Add($"-u {QuoteArgumentIfNeeded(config.User)}");

        // Restart policy
        if (!string.IsNullOrEmpty(config.RestartPolicy))
          args.Add($"--restart {QuoteArgumentIfNeeded(config.RestartPolicy)}");

        // Hostname
        if (!string.IsNullOrEmpty(config.Hostname))
          args.Add($"--hostname {QuoteArgumentIfNeeded(config.Hostname)}");

        // Static IPv4 address
        if (!string.IsNullOrEmpty(config.Ipv4Address))
          args.Add($"--ip {QuoteArgumentIfNeeded(config.Ipv4Address)}");

        // Static IPv6 address
        if (!string.IsNullOrEmpty(config.Ipv6Address))
          args.Add($"--ip6 {QuoteArgumentIfNeeded(config.Ipv6Address)}");

        // Memory limit
        if (config.MemoryLimit.HasValue)
          args.Add($"--memory {config.MemoryLimit.Value}");

        // CPU shares
        if (config.CpuShares.HasValue)
          args.Add($"--cpu-shares {config.CpuShares.Value}");

        // Privileged mode
        if (config.Privileged)
          args.Add("--privileged");

        // Auto remove
        if (config.AutoRemove)
          args.Add("--rm");

        // Links (legacy Docker feature)
        if (config.Links != null)
          foreach (var link in config.Links)
            args.Add($"--link {QuoteArgumentIfNeeded(link)}");

        // Image (required)
        args.Add(QuoteArgumentIfNeeded(config.Image));

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
          return CommandResponse<ContainerCreateResult>.Fail(
              result.Error ?? "Container creation failed",
              ErrorCodes.Container.CreateFailed,
              CreateErrorContext(context, "CreateContainer", result),
              result.ExitCode);
        }

        var containerId = result.Output.Trim();

        return CommandResponse<ContainerCreateResult>.Ok(
            new ContainerCreateResult { Id = containerId });
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerCreateResult>.Fail(
            ex.Message,
            ErrorCodes.Container.CreateFailed);
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
        var result = await ExecuteCommandAsync($"start {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container start failed",
              ErrorCodes.Container.StartFailed,
              CreateErrorContext(context, "StartContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StartFailed);
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
          args += $" -t {timeout.Value}";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container stop failed",
              ErrorCodes.Container.StopFailed,
              CreateErrorContext(context, "StopContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StopFailed);
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
          args += $" -t {timeout.Value}";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container restart failed",
              ErrorCodes.Container.RestartFailed,
              CreateErrorContext(context, "RestartContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RestartFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PauseAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"pause {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container pause failed",
              ErrorCodes.Container.PauseFailed,
              CreateErrorContext(context, "PauseContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.PauseFailed);
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
        var result = await ExecuteCommandAsync($"unpause {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container unpause failed",
              ErrorCodes.Container.UnpauseFailed,
              CreateErrorContext(context, "UnpauseContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UnpauseFailed);
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
        var args = $"kill --signal {QuoteArgumentIfNeeded(signal)} {QuoteArgumentIfNeeded(containerId)}";
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container kill failed",
              ErrorCodes.Container.KillFailed,
              CreateErrorContext(context, "KillContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.KillFailed);
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
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Container remove failed",
              ErrorCodes.Container.RemoveFailed,
              CreateErrorContext(context, "RemoveContainer", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RemoveFailed);
      }
    }

    #endregion
  }
}
