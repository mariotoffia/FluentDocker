#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of IContainerDriver.
  /// Core lifecycle and information operations.
  /// </summary>
  public partial class PodmanCliContainerDriver(IPodmanBinaryResolver binaryResolver) : PodmanCliDriverBase(binaryResolver), IContainerDriver
  {

    #region Lifecycle Operations

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
        DriverContext context, ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildCreateArgs("create", config);
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerCreateResult>.Fail(
              ErrorOrDefault(result, "Container create failed"), FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
              CreateErrorContext(context, "CreateContainer", result), result.ExitCode);

        return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult
        {
          Id = result.Output?.Trim(),
          Name = config.Name
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerCreateResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Container.CreateFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerRunResult>> RunAsync(
        DriverContext context, ContainerCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      string cidFile = null;
      try
      {
        cidFile = Path.Combine(DirectoryHelper.GetTempPath(), $"podman-cid-{Guid.NewGuid():N}");
        var args = BuildCreateArgsWithCidFile("run", config, config.Detach, cidFile);

        // run can be inherently long (it waits for a non-detached container to finish);
        // honor only caller cancellation, not the buffered control-plane timeout.
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (config.Detach)
        {
          if (!result.Success)
            return CommandResponse<ContainerRunResult>.Fail(
                ErrorOrDefault(result, "Container run failed"), FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
                CreateErrorContext(context, "RunContainer", result), result.ExitCode);

          return CommandResponse<ContainerRunResult>.Ok(new ContainerRunResult
          {
            Id = result.Output?.Trim()
          });
        }

        var containerId = TryReadCidFile(cidFile);
        if (!result.Success && string.IsNullOrEmpty(containerId))
          return CommandResponse<ContainerRunResult>.Fail(
              ErrorOrDefault(result, "Container run failed"), FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
              CreateErrorContext(context, "RunContainer", result), result.ExitCode);

        return CommandResponse<ContainerRunResult>.Ok(new ContainerRunResult
        {
          Id = containerId,
          Output = MergeOutputAndError(result.Output, result.Error),
          ExitCode = result.ExitCode
        });
      }
      catch (OperationCanceledException)
      {
        await RemoveCidFileContainerAsync(context, cidFile).ConfigureAwait(false);
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerRunResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Container.CreateFailed));
      }
      finally
      {
        if (cidFile != null && File.Exists(cidFile))
        {
          try
          {
            File.Delete(cidFile);
          }
          catch
          {
          }
        }
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"start {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container start failed"), FailureCode(result.Error, ErrorCodes.Container.StartFailed),
              CreateErrorContext(context, "StartContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.StartFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopAsync(
        DriverContext context, string containerId, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "stop";
        if (timeout.HasValue)
          args += $" -t {timeout.Value.ToString(CultureInfo.InvariantCulture)}";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        // stop waits up to the grace period for the container to exit — inherently long.
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container stop failed"), FailureCode(result.Error, ErrorCodes.Container.StopFailed),
              CreateErrorContext(context, "StopContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.StopFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RestartAsync(
        DriverContext context, string containerId, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "restart";
        if (timeout.HasValue)
          args += $" -t {timeout.Value.ToString(CultureInfo.InvariantCulture)}";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        // restart waits up to the grace period for the container to stop — inherently long.
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container restart failed"), FailureCode(result.Error, ErrorCodes.Container.RestartFailed),
              CreateErrorContext(context, "RestartContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.RestartFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PauseAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"pause {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container pause failed"), FailureCode(result.Error, ErrorCodes.Container.PauseFailed),
              CreateErrorContext(context, "PauseContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.PauseFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnpauseAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"unpause {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container unpause failed"), FailureCode(result.Error, ErrorCodes.Container.UnpauseFailed),
              CreateErrorContext(context, "UnpauseContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.UnpauseFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> KillAsync(
        DriverContext context, string containerId, string signal = "SIGKILL",
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context, $"kill --signal {QuotePositionalArgument(signal, nameof(signal))} {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container kill failed"), FailureCode(result.Error, ErrorCodes.Container.KillFailed),
              CreateErrorContext(context, "KillContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.KillFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string containerId,
        bool force = false, bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "rm";
        if (force)
          args += " -f";
        if (removeVolumes)
          args += " -v";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Container remove failed"), FailureCode(result.Error, ErrorCodes.Container.RemoveFailed),
              CreateErrorContext(context, "RemoveContainer", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.RemoveFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerWaitResult>> WaitAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // wait blocks until the container exits — inherently long; honor only caller cancellation.
        var result = await ExecuteUnboundedCommandAsync(context, $"wait {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ContainerWaitResult>.Fail(
              ErrorOrDefault(result, "Container wait failed"), FailureCode(result.Error, ErrorCodes.Container.WaitFailed),
              CreateErrorContext(context, "WaitContainer", result), result.ExitCode);

        if (!int.TryParse(result.Output?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode))
          return CommandResponse<ContainerWaitResult>.Fail(
              $"Unable to parse container wait exit code: {result.Output}",
              ErrorCodes.Container.WaitFailed,
              CreateErrorContext(context, "WaitContainer", result),
              result.ExitCode);
        return CommandResponse<ContainerWaitResult>.Ok(new ContainerWaitResult
        {
          ExitCode = exitCode
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerWaitResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Container.WaitFailed));
      }
    }

    #endregion

    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Container>> InspectAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context, $"inspect {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
          var errorCode = IsContainerNotFound(result.Error)
              ? ErrorCodes.Container.NotFound
              : FailureCode(result.Error, ErrorCodes.Container.InspectFailed);
          return CommandResponse<Container>.Fail(
              ErrorOrDefault(result, "Container inspect failed"), errorCode,
              CreateErrorContext(context, "InspectContainer", result), result.ExitCode);
        }

        var container = ParseContainerInspect(result.Output);
        if (string.IsNullOrEmpty(container.Id))
          return CommandResponse<Container>.Fail(
              $"Container '{containerId}' was not found", ErrorCodes.Container.NotFound,
              CreateErrorContext(context, "InspectContainer", result), result.ExitCode);
        return CommandResponse<Container>.Ok(container);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Container>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Container.InspectFailed));
      }
    }

    private static bool IsContainerNotFound(string error) =>
        error?.Contains("no such object", StringComparison.OrdinalIgnoreCase) == true
        || error?.Contains("no such container", StringComparison.OrdinalIgnoreCase) == true;

    private async Task RemoveCidFileContainerAsync(DriverContext context, string cidFile)
    {
      var containerId = TryReadCidFile(cidFile);
      if (string.IsNullOrWhiteSpace(containerId))
        return;

      try
      {
        // ponytail: 5s cleanup budget on cancel; raise if slow daemons legitimately need longer.
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ExecuteCommandAsync(
            context,
            $"rm -f {QuotePositionalArgument(containerId, nameof(containerId))}",
            cleanupCts.Token).ConfigureAwait(false);
      }
      catch
      {
        // best effort cancellation cleanup
      }
    }

    private static string TryReadCidFile(string cidFile)
    {
      if (string.IsNullOrEmpty(cidFile) || !File.Exists(cidFile))
        return null;
      try
      {
        return File.ReadAllText(cidFile).Trim();
      }
      catch
      {
        return null;
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context, ContainerListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildListArgs(filter);
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<Container>>.Fail(
              ErrorOrDefault(result, "Container list failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "ListContainers", result), result.ExitCode);

        var containers = ParseContainerList(result.Output);
        return CommandResponse<IList<Container>>.Ok(containers);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Container>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    #endregion
  }
}
