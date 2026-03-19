using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of IVolumeDriver.
  /// </summary>
  public class PodmanCliVolumeDriver : PodmanCliDriverBase, IVolumeDriver
  {
    public PodmanCliVolumeDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VolumeCreateResult>> CreateAsync(
        DriverContext context, VolumeCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "volume create";
        if (!string.IsNullOrEmpty(config.Driver))
          args += $" --driver {config.Driver}";

        foreach (var opt in config.DriverOpts)
          args += $" --opt {opt.Key}={opt.Value}";

        foreach (var label in config.Labels)
          args += $" --label {label.Key}={label.Value}";

        if (!string.IsNullOrEmpty(config.Name))
          args += $" {config.Name}";

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<VolumeCreateResult>.Fail(
              result.Error ?? "Volume create failed", ErrorCodes.Volume.CreateFailed);

        return CommandResponse<VolumeCreateResult>.Ok(new VolumeCreateResult
        {
          Name = result.Output?.Trim() ?? config.Name,
          Driver = config.Driver
        });
      }
      catch (Exception ex)
      {
        return CommandResponse<VolumeCreateResult>.Fail(ex.Message, ErrorCodes.Volume.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string volumeName, bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = force ? $"volume rm -f {volumeName}" : $"volume rm {volumeName}";
        var result = await ExecuteCommandAsync(args, cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Volume remove failed", ErrorCodes.Volume.RemoveFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Volume.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Volume>>> ListAsync(
        DriverContext context, VolumeListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "volume ls --format json";
        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter name={filter.Name}";

          if (filter.Labels != null)
            foreach (var label in filter.Labels)
              args += $" --filter label={label.Key}={label.Value}";
        }

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<IList<Volume>>.Fail(
              result.Error ?? "Volume list failed", ErrorCodes.General.Unknown);

        var volumes = ParseVolumeList(result.Output);
        return CommandResponse<IList<Volume>>.Ok(volumes);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Volume>>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Volume>> InspectAsync(
        DriverContext context, string volumeName,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"volume inspect {volumeName}", cancellationToken);
        if (!result.Success)
          return CommandResponse<Volume>.Fail(
              result.Error ?? "Volume inspect failed", ErrorCodes.Volume.InspectFailed);

        var volume = ParseVolumeInspect(result.Output);
        return CommandResponse<Volume>.Ok(volume);
      }
      catch (Exception ex)
      {
        return CommandResponse<Volume>.Fail(ex.Message, ErrorCodes.Volume.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VolumePruneResult>> PruneAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("volume prune -f", cancellationToken);
        if (!result.Success)
          return CommandResponse<VolumePruneResult>.Fail(
              result.Error ?? "Volume prune failed", ErrorCodes.Volume.PruneFailed);

        return CommandResponse<VolumePruneResult>.Ok(
            CliPruneOutputParser.ParseVolumePruneOutput(result.Output));
      }
      catch (Exception ex)
      {
        return CommandResponse<VolumePruneResult>.Fail(ex.Message, ErrorCodes.Volume.PruneFailed);
      }
    }

    #region JSON Parsing

    private static List<Volume> ParseVolumeList(string json)
    {
      var volumes = new List<Volume>();
      if (string.IsNullOrWhiteSpace(json))
        return volumes;

      try
      {
        // Podman outputs one JSON object per line or a JSON array
        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
            volumes.Add(ParseVolumeFromToken(token));
        }
        else
        {
          foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            volumes.Add(ParseVolumeFromToken(JsonHelper.ParseElement(line.Trim())));
        }
      }
      catch { /* Return partial results */ }

      return volumes;
    }

    private static Volume ParseVolumeFromToken(JsonElement token)
    {
      var volume = new Volume
      {
        Name = token.GetStringOrDefault("Name"),
        Driver = token.GetStringOrDefault("Driver"),
        Scope = token.GetStringOrDefault("Scope"),
        Mountpoint = token.GetStringOrDefault("Mountpoint")
      };

      var labels = token.Prop("Labels");
      if (labels.HasValue && labels.Value.ValueKind == JsonValueKind.Object)
        volume.Labels = token.GetStringDictionary("Labels");

      var options = token.Prop("Options");
      if (options.HasValue && options.Value.ValueKind == JsonValueKind.Object)
        volume.Options = token.GetStringDictionary("Options");

      return volume;
    }

    private static Volume ParseVolumeInspect(string json)
    {
      try
      {
        var trimmed = json.Trim();
        JsonElement token;
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          using var enumerator = root.EnumerateArray();
          if (!enumerator.MoveNext())
            return new Volume();
          token = enumerator.Current;
        }
        else
        {
          token = JsonHelper.ParseElement(trimmed);
        }

        return ParseVolumeFromToken(token);
      }
      catch (Exception ex)
      {
        Logger.Log($"Podman volume inspect parsing failed: {ex.Message}");
        return new Volume();
      }
    }

    #endregion
  }
}
