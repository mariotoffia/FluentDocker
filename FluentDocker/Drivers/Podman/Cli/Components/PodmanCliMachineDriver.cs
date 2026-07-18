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
            context, BuildInitArgs(config), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine init failed"), FailureCode(result.Error, ErrorCodes.Machine.InitFailed),
              CreateErrorContext(context, "InitMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Machine.InitFailed));
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
            : $"machine start {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine start failed"), FailureCode(result.Error, ErrorCodes.Machine.StartFailed),
              CreateErrorContext(context, "StartMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Machine.StartFailed));
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
            : $"machine stop {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine stop failed"), FailureCode(result.Error, ErrorCodes.Machine.StopFailed),
              CreateErrorContext(context, "StopMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Machine.StopFailed));
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
        else
        {
          var state = await InspectAsync(context, name, cancellationToken).ConfigureAwait(false);
          if (!state.Success)
            return CommandResponse<Unit>.Fail(
                $"Machine remove state check failed: {state.Error}",
                ErrorCodes.Machine.RemoveFailed, state.ErrorContext, state.ExitCode, state.Output);
          if (IsActiveMachineState(state.Data?.State))
            return CommandResponse<Unit>.Fail(
                $"Podman machine '{MachineNameForMessage(name, state.Data)}' is {state.Data.State}; stop it first or call RemoveAsync with force: true.",
                ErrorCodes.Machine.RemoveFailed);
          if (string.IsNullOrWhiteSpace(state.Data?.State))
            return CommandResponse<Unit>.Fail(
                $"Podman machine '{MachineNameForMessage(name, state.Data)}' state could not be determined; stop it first or call RemoveAsync with force: true.",
                ErrorCodes.Machine.RemoveFailed);
          // -f is required even for a stopped machine (plain `rm` always aborts at podman's
          // interactive prompt when stdin is closed). Inspect-then-`rm -f` is inherently
          // TOCTOU: a machine started externally between the check and the rm is still
          // force-removed — documented on IPodmanMachineDriver.RemoveAsync.
          args += " -f";
        }
        if (!string.IsNullOrEmpty(name))
          args += $" {QuotePositionalArgument(name, nameof(name))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine remove failed"), FailureCode(result.Error, ErrorCodes.Machine.RemoveFailed),
              CreateErrorContext(context, "RemoveMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Machine.RemoveFailed));
      }
    }

    private static bool IsActiveMachineState(string state) =>
        string.Equals(state, "running", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "starting", StringComparison.OrdinalIgnoreCase);

    private static string MachineNameForMessage(string requestedName, MachineInspectResult result) =>
        string.IsNullOrWhiteSpace(requestedName)
            ? string.IsNullOrWhiteSpace(result?.Name) ? "default" : result.Name
            : requestedName;

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
          args += $" {QuotePositionalArgument(name, nameof(name))}";
        if (!string.IsNullOrEmpty(command))
          args += $" {QuotePositionalArgument(command, nameof(command))}";

        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Machine SSH failed"), FailureCode(result.Error, ErrorCodes.Machine.SshFailed),
              CreateErrorContext(context, "MachineSsh", result), result.ExitCode);

        return CommandResponse<string>.Ok(result.Output?.TrimEnd());
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Machine.SshFailed));
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
            context, BuildSetArgs(config, name), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Machine set failed"), FailureCode(result.Error, ErrorCodes.Machine.SetFailed),
              CreateErrorContext(context, "SetMachine", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Machine.SetFailed));
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

      foreach (var vol in OrEmpty(config.Volumes))
        args += $" -v {QuoteArgumentIfNeeded(vol)}";

      if (!string.IsNullOrEmpty(config.Name))
        args += $" {QuotePositionalArgument(config.Name, nameof(config.Name))}";

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
        args += $" {QuotePositionalArgument(name, nameof(name))}";

      return args;
    }

    #endregion
  }
}
