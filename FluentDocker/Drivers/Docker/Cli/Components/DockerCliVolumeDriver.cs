using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IVolumeDriver.
  /// </summary>
  public class DockerCliVolumeDriver : DockerCliDriverBase, IVolumeDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliVolumeDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VolumeCreateResult>> CreateAsync(
        DriverContext context,
        VolumeCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "volume", "create" };

        if (!string.IsNullOrEmpty(config.Driver))
          args.Add($"--driver {QuoteArgumentIfNeeded(config.Driver)}");

        if (config.Labels != null)
        {
          foreach (var label in config.Labels)
            args.Add($"--label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}");
        }

        if (config.DriverOpts != null)
        {
          foreach (var opt in config.DriverOpts)
            args.Add($"--opt {QuoteArgumentIfNeeded($"{opt.Key}={opt.Value}")}");
        }

        if (!string.IsNullOrEmpty(config.Name))
          args.Add(QuotePositionalArgument(config.Name, nameof(config.Name)));

        var result = await ExecuteCommandAsync(context, string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<VolumeCreateResult>.Fail(
              ErrorOrDefault(result, "Volume creation failed"),
              FailureCode(result.Error, ErrorCodes.Volume.CreateFailed),
              CreateErrorContext(context, "CreateVolume", result),
              result.ExitCode);
        }

        return CommandResponse<VolumeCreateResult>.Ok(
            new VolumeCreateResult { Name = result.Output.Trim() });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<VolumeCreateResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Volume.CreateFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string volumeName,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "volume rm";
        if (force)
          args += " --force";
        args += $" {QuotePositionalArgument(volumeName, nameof(volumeName))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Volume removal failed"),
              FailureCode(result.Error, ErrorCodes.Volume.RemoveFailed),
              CreateErrorContext(context, "RemoveVolume", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Volume.RemoveFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Volume>>> ListAsync(
        DriverContext context,
        VolumeListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "volume ls --format \"{{json .}}\"";

        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";

          if (filter.Labels != null)
          {
            foreach (var label in filter.Labels)
              args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
          }
        }

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Volume>>.Fail(
              ErrorOrDefault(result, "Volume list failed"),
              FailureCode(result.Error, ErrorCodes.Volume.ListFailed),
              CreateErrorContext(context, "ListVolumes", result),
              result.ExitCode);
        }

        if (!DockerCliJsonLineParser.TryParse(
                result.Output,
                Logger,
                "Volume list JSON parsing failed",
                out List<Volume> volumes,
                out var parseError))
        {
          return CommandResponse<IList<Volume>>.Fail(parseError, ErrorCodes.Volume.ListFailed);
        }

        return CommandResponse<IList<Volume>>.Ok(volumes);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Volume>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Volume.ListFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Volume>> InspectAsync(
        DriverContext context,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"volume inspect {QuotePositionalArgument(volumeName, nameof(volumeName))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Volume>.Fail(
              ErrorOrDefault(result, "Volume inspect failed"),
              result.Error?.Contains("No such volume", StringComparison.OrdinalIgnoreCase) == true
                  ? ErrorCodes.Volume.NotFound
                  : FailureCode(result.Error, ErrorCodes.Volume.InspectFailed),
              CreateErrorContext(context, "InspectVolume", result),
              result.ExitCode);
        }

        if (!JsonHelper.TryDeserialize<List<Volume>>(result.Output, out var volumes, out var parseError))
        {
          Logger.LogError(parseError, "Volume inspect JSON parsing failed");
          var error = parseError == null
              ? "Volume inspect JSON parsing failed"
              : $"Volume inspect JSON parsing failed: {parseError.Message}";
          return CommandResponse<Volume>.Fail(error, ErrorCodes.Volume.InspectFailed);
        }
        var volume = volumes?.FirstOrDefault();
        return volume == null
            ? CommandResponse<Volume>.Fail($"Volume {volumeName} not found", ErrorCodes.Volume.NotFound)
            : CommandResponse<Volume>.Ok(volume);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Volume>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Volume.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VolumePruneResult>> PruneAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "volume prune --force", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<VolumePruneResult>.Fail(
              ErrorOrDefault(result, "Volume prune failed"),
              FailureCode(result.Error, ErrorCodes.Volume.PruneFailed));
        }

        return CommandResponse<VolumePruneResult>.Ok(
            CliPruneOutputParser.ParseVolumePruneOutput(result.Output));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<VolumePruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Volume.PruneFailed));
      }
    }
  }
}
