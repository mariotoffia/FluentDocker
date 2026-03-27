using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

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
          args.Add($"--driver {config.Driver}");

        if (config.Labels != null)
        {
          foreach (var label in config.Labels)
            args.Add($"--label {label.Key}={label.Value}");
        }

        if (config.DriverOpts != null)
        {
          foreach (var opt in config.DriverOpts)
            args.Add($"--opt {opt.Key}={opt.Value}");
        }

        args.Add(config.Name);

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<VolumeCreateResult>.Fail(
              result.Error ?? "Volume creation failed",
              ErrorCodes.Volume.CreateFailed,
              CreateErrorContext(context, "CreateVolume", result),
              result.ExitCode);
        }

        return CommandResponse<VolumeCreateResult>.Ok(
            new VolumeCreateResult { Name = result.Output.Trim() });
      }
      catch (Exception ex)
      {
        return CommandResponse<VolumeCreateResult>.Fail(ex.Message, ErrorCodes.Volume.CreateFailed);
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
        args += $" {volumeName}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Volume removal failed",
              ErrorCodes.Volume.RemoveFailed,
              CreateErrorContext(context, "RemoveVolume", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Volume.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Volume>>> ListAsync(
        DriverContext context,
        VolumeListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "volume ls --format \"{{json .}}\"";

        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter name={filter.Name}";

          if (filter.Labels != null)
          {
            foreach (var label in filter.Labels)
              args += $" --filter label={label.Key}={label.Value}";
          }
        }

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Volume>>.Fail(
              result.Error ?? "Volume list failed",
              ErrorCodes.General.Unknown,
              CreateErrorContext(context, "ListVolumes", result),
              result.ExitCode);
        }

        var volumes = new List<Volume>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
          try
          {
            var volume = JsonSerializer.Deserialize<Volume>(line, JsonHelper.CaseInsensitiveOptions);
            if (volume != null)
              volumes.Add(volume);
          }
          catch (Exception ex)
          {
            Logger.Log($"Volume list JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<Volume>>.Ok(volumes);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Volume>>.Fail(ex.Message, ErrorCodes.General.Unknown);
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
        var result = await ExecuteCommandAsync($"volume inspect {volumeName}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Volume>.Fail(
              result.Error ?? "Volume inspect failed",
              ErrorCodes.Volume.InspectFailed,
              CreateErrorContext(context, "InspectVolume", result),
              result.ExitCode);
        }

        var volumes = JsonSerializer.Deserialize<List<Volume>>(result.Output, JsonHelper.CaseInsensitiveOptions);
        return CommandResponse<Volume>.Ok(volumes?.FirstOrDefault() ?? new Volume());
      }
      catch (Exception ex)
      {
        return CommandResponse<Volume>.Fail(ex.Message, ErrorCodes.Volume.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VolumePruneResult>> PruneAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("volume prune --force", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<VolumePruneResult>.Fail(
              result.Error ?? "Volume prune failed",
              ErrorCodes.Volume.PruneFailed);
        }

        return CommandResponse<VolumePruneResult>.Ok(
            CliPruneOutputParser.ParseVolumePruneOutput(result.Output));
      }
      catch (Exception ex)
      {
        return CommandResponse<VolumePruneResult>.Fail(ex.Message, ErrorCodes.Volume.PruneFailed);
      }
    }
  }
}
