using System;
using System.Globalization;
using System.IO;
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
      // Use --cidfile for race-free container ID discovery and cancellation cleanup.
      string cidFile = null;
      try
      {
        if (StartsWithDash(config.Image))
          return FailInvalidLeadingDash<ContainerRunResult>("Container image");
        // Temp path, not CWD: consumers may run with a read-only working directory.
        cidFile = Path.Combine(Path.GetTempPath(), $"docker-cid-{Guid.NewGuid():N}");
        var args = BuildCreateArgs("run", config, config.Detach, cidFile);

        // `docker run` blocks until the container exits when not detached, so it must honor
        // only caller cancellation (the default buffered timeout would falsely abort a
        // legitimately long-running foreground container).
        var result = await ExecuteUnboundedCommandAsync(context, string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (config.Detach)
        {
          if (!result.Success)
          {
            return CommandResponse<ContainerRunResult>.Fail(
                ErrorOrDefault(result, "Container run failed"),
                FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
                CreateErrorContext(context, "RunContainer", result),
                result.ExitCode);
          }

          return CommandResponse<ContainerRunResult>.Ok(new ContainerRunResult { Id = result.Output.Trim() });
        }

        var containerId = TryReadCidFile(cidFile);
        if (!result.Success && string.IsNullOrEmpty(containerId))
        {
          return CommandResponse<ContainerRunResult>.Fail(
              ErrorOrDefault(result, "Container run failed"),
              FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
              CreateErrorContext(context, "RunContainer", result),
              result.ExitCode);
        }

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
        return CommandResponse<ContainerRunResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.CreateFailed));
      }
      finally
      {
        if (cidFile != null && File.Exists(cidFile))
        {
          try
          {
            File.Delete(cidFile);
          }
          catch (IOException)
          {
          }
          catch (UnauthorizedAccessException)
          {
          }
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
        var result = await ExecuteUnboundedCommandAsync(context, $"wait {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerWaitResult>.Fail(
              ErrorOrDefault(result, "Container wait failed"),
              FailureCode(result.Error, ErrorCodes.Container.WaitFailed),
              CreateErrorContext(context, "WaitContainer", result),
              result.ExitCode);
        }

        if (!int.TryParse(result.Output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode))
        {
          return CommandResponse<ContainerWaitResult>.Fail(
              $"Container wait returned a non-integer exit code: {result.Output.Trim()}",
              ErrorCodes.Container.WaitFailed,
              CreateErrorContext(context, "WaitContainer", result),
              result.ExitCode);
        }

        return CommandResponse<ContainerWaitResult>.Ok(
            new ContainerWaitResult { ExitCode = exitCode });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerWaitResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.WaitFailed));
      }
    }

    private async Task RemoveCidFileContainerAsync(DriverContext context, string cidFile)
    {
      // ponytail: cidfile reconciliation only; a CLI killed between daemon-create and cidfile-write
      // still orphans (Docker's own race). Add label-based sweep if that gap must be closed.
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

    #endregion
  }
}
