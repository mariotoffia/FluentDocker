#nullable disable warnings
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of ISystemDriver.
  /// Adapted for Podman's daemonless architecture.
  /// </summary>
  public partial class PodmanCliSystemDriver : PodmanCliDriverBase, ISystemDriver
  {
    /// <summary>Creates a new instance with the specified binary resolver.</summary>
    public PodmanCliSystemDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "info --format json", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<SystemInfo>.Fail(
              ErrorOrDefault(result, "System info failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "SystemInfo", result), result.ExitCode);

        var info = ParseSystemInfo(result.Output);
        info.PopulateMeta();
        return CommandResponse<SystemInfo>.Ok(info);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "version --format json", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<VersionInfo>.Fail(
              ErrorOrDefault(result, "Version check failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "SystemVersion", result), result.ExitCode);

        var version = ParseVersionInfo(result.Output);
        version.PopulateMeta();
        return CommandResponse<VersionInfo>.Ok(version);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<VersionInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PingAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        // Podman is daemonless; verify it works by running 'podman info'. A liveness probe
        // against a wedged machine/VM must fail in seconds, not hang the readiness loop for the
        // 5-min buffered default (DCLI-MAJ-3). Cap at 10s (honor a smaller caller RequestTimeout).
        var probeCeiling = TimeSpan.FromSeconds(10);
        var probeTimeout = context?.RequestTimeout is { } rt && rt < probeCeiling ? rt : probeCeiling;
        var result = await ExecuteCommandAsync(context, "info", probeTimeout, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Podman is not reachable"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "SystemPing", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
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
    public Task<CommandResponse<bool>> IsWindowsEngineAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      // Podman always uses Linux containers (even on macOS/Windows via VM)
      return Task.FromResult(CommandResponse<bool>.Ok(false));
    }

    /// <inheritdoc />
    public Task<CommandResponse<bool>> IsLinuxEngineAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      // Podman always uses Linux containers
      return Task.FromResult(CommandResponse<bool>.Ok(true));
    }

    #endregion

    #region Maintenance Operations

    /// <inheritdoc />
    public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "system df --format json", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<DiskUsageInfo>.Fail(
              ErrorOrDefault(result, "Disk usage failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "SystemDiskUsage", result), result.ExitCode);

        var info = ParseDiskUsageOutput(result.Output);
        return CommandResponse<DiskUsageInfo>.Ok(info);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<DiskUsageInfo>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
        DriverContext context, SystemPruneConfig? config = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildSystemPruneArgs(config);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<SystemPruneResult>.Fail(
              ErrorOrDefault(result, "System prune failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "SystemPrune", result), result.ExitCode);

        return CommandResponse<SystemPruneResult>.Ok(
            CliPruneOutputParser.ParseSystemPruneOutput(result.Output));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemPruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    #endregion

    #region Daemon Operations (Not applicable to Podman)

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Podman is daemonless and does not support daemon switching",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Podman is daemonless and always runs Linux containers",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      // Podman does not support Windows containers
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Podman does not support Windows containers",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    #endregion

    #region Argument Building

    /// <summary>
    /// Builds the CLI arguments string for <c>podman system prune</c>.
    /// </summary>
    public static string BuildSystemPruneArgs(SystemPruneConfig config)
    {
      var args = "system prune -f";
      if (config?.All == true)
        args += " -a";
      if (config?.Volumes == true)
        args += " --volumes";
      if (config?.Filter != null)
      {
        foreach (var f in config.Filter)
          args += $" --filter {QuoteArgumentIfNeeded($"{f.Key}={f.Value}")}";
      }
      return args;
    }

    #endregion
  }
}
