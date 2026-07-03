using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of machine (VM) management operations.
  /// </summary>
  public partial class PodmanCliMachineDriver(IPodmanBinaryResolver binaryResolver) : PodmanCliDriverBase(binaryResolver), IPodmanMachineDriver
  {

    #region Lifecycle

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> InitAsync(
        DriverContext context, MachineInitConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);

      try
      {
        var result = await ExecuteUnboundedCommandAsync(
            BuildInitArgs(config), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine init failed"), ErrorCodes.Machine.InitFailed,
              CreateErrorContext(context, "InitMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.InitFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = string.IsNullOrEmpty(name)
            ? "machine start"
            : $"machine start {QuoteArgumentIfNeeded(name)}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine start failed"), ErrorCodes.Machine.StartFailed,
              CreateErrorContext(context, "StartMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.StartFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = string.IsNullOrEmpty(name)
            ? "machine stop"
            : $"machine stop {QuoteArgumentIfNeeded(name)}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine stop failed"), ErrorCodes.Machine.StopFailed,
              CreateErrorContext(context, "StopMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.StopFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string name = null, bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "machine rm";
        if (force)
          args += " -f";
        if (!string.IsNullOrEmpty(name))
          args += $" {QuoteArgumentIfNeeded(name)}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine remove failed"), ErrorCodes.Machine.RemoveFailed,
              CreateErrorContext(context, "RemoveMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.RemoveFailed);
      }
    }

    #endregion

    #region Interaction

    /// <inheritdoc />
    public async Task<CommandResponse<string>> SshAsync(
        DriverContext context, string name = null, string command = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "machine ssh";
        if (!string.IsNullOrEmpty(name))
          args += $" {QuoteArgumentIfNeeded(name)}";
        if (!string.IsNullOrEmpty(command))
          args += $" {QuoteArgumentIfNeeded(command)}";

        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Machine SSH failed"), ErrorCodes.Machine.SshFailed,
              CreateErrorContext(context, "MachineSsh", result), result.ExitCode);

        return CommandResponse<string>.Ok(result.Output?.TrimEnd());
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Machine.SshFailed);
      }
    }

    #endregion

    #region Configuration

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SetAsync(
        DriverContext context, MachineSetConfig config, string name = null,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);

      try
      {
        var result = await ExecuteCommandAsync(
            BuildSetArgs(config, name), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine set failed"), ErrorCodes.Machine.SetFailed,
              CreateErrorContext(context, "SetMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.SetFailed);
      }
    }

    #endregion

    #region Argument Building

    internal static string BuildInitArgs(MachineInitConfig config)
    {
      var args = "machine init";

      if (config.Cpus.HasValue)
        args += $" --cpus {QuoteArgumentIfNeeded(config.Cpus.Value.ToString(CultureInfo.InvariantCulture))}";
      if (config.DiskSizeGiB.HasValue)
        args += $" --disk-size {QuoteArgumentIfNeeded(config.DiskSizeGiB.Value.ToString(CultureInfo.InvariantCulture))}";
      if (config.MemoryMiB.HasValue)
        args += $" --memory {QuoteArgumentIfNeeded(config.MemoryMiB.Value.ToString(CultureInfo.InvariantCulture))}";
      if (config.Rootful)
        args += " --rootful";
      if (!string.IsNullOrEmpty(config.Image))
        args += $" --image {QuoteArgumentIfNeeded(config.Image)}";
      if (!string.IsNullOrEmpty(config.Username))
        args += $" --username {QuoteArgumentIfNeeded(config.Username)}";
      if (config.Now)
        args += " --now";

      foreach (var vol in config.Volumes)
        args += $" -v {QuoteArgumentIfNeeded(vol)}";

      if (!string.IsNullOrEmpty(config.Name))
        args += $" {QuoteArgumentIfNeeded(config.Name)}";

      return args;
    }

    internal static string BuildSetArgs(MachineSetConfig config, string name = null)
    {
      var args = "machine set";

      if (config.Cpus.HasValue)
        args += $" --cpus {QuoteArgumentIfNeeded(config.Cpus.Value.ToString(CultureInfo.InvariantCulture))}";
      if (config.DiskSizeGiB.HasValue)
        args += $" --disk-size {QuoteArgumentIfNeeded(config.DiskSizeGiB.Value.ToString(CultureInfo.InvariantCulture))}";
      if (config.MemoryMiB.HasValue)
        args += $" --memory {QuoteArgumentIfNeeded(config.MemoryMiB.Value.ToString(CultureInfo.InvariantCulture))}";
      if (config.Rootful.HasValue)
        args += config.Rootful.Value ? " --rootful" : " --rootful=false";

      if (!string.IsNullOrEmpty(name))
        args += $" {QuoteArgumentIfNeeded(name)}";

      return args;
    }

    #endregion
  }
}
