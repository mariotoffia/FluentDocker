using System;
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
      // Use --cidfile for race-free container ID discovery in non-detached mode.
      string cidFile = null;
      try
      {
        if (StartsWithDash(config.Image))
          return FailInvalidLeadingDash<ContainerRunResult>("Container image");
        if (!config.Detach)
        {
          cidFile = Path.Combine(Path.GetTempPath(), $"docker-cid-{Guid.NewGuid():N}");
        }
        var args = BuildCreateArgs("run", config, config.Detach, cidFile);

        // `docker run` blocks until the container exits when not detached, so it must honor
        // only caller cancellation (the default buffered timeout would falsely abort a
        // legitimately long-running foreground container).
        var result = await ExecuteUnboundedCommandAsync(context, string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerRunResult>.Fail(
              ErrorOrDefault(result, "Container run failed"),
              FailureCode(result.Error, ErrorCodes.Container.CreateFailed),
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
          runResult.Output = string.IsNullOrEmpty(result.Error)
              ? result.Output
              : result.Output + result.Error;

          // Read container ID from --cidfile (race-free, set earlier in args).
          if (cidFile != null && File.Exists(cidFile))
          {
            runResult.Id = (await File.ReadAllTextAsync(cidFile, cancellationToken)).Trim();
          }
        }

        return CommandResponse<ContainerRunResult>.Ok(runResult);
      }
      catch (OperationCanceledException)
      {
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
        var result = await ExecuteUnboundedCommandAsync(context, $"wait {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerWaitResult>.Fail(
              ErrorOrDefault(result, "Container wait failed"),
              FailureCode(result.Error, ErrorCodes.Container.WaitFailed),
              CreateErrorContext(context, "WaitContainer", result),
              result.ExitCode);
        }

        if (!int.TryParse(result.Output.Trim(), out var exitCode))
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

    #endregion
  }
}
