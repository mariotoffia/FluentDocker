#nullable disable warnings
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
    /// <summary>Creates a new instance with the specified binary resolver.</summary>
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
          args += $" --driver {QuoteArgumentIfNeeded(config.Driver)}";

        foreach (var opt in OrEmpty(config.DriverOpts))
          args += $" --opt {QuoteArgumentIfNeeded($"{opt.Key}={opt.Value}")}";

        foreach (var label in OrEmpty(config.Labels))
          args += $" --label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}";

        if (!string.IsNullOrEmpty(config.Name))
          args += $" {QuotePositionalArgument(config.Name, nameof(config.Name))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<VolumeCreateResult>.Fail(
              ErrorOrDefault(result, "Volume create failed"), FailureCode(result.Error, ErrorCodes.Volume.CreateFailed),
              CreateErrorContext(context, "CreateVolume", result), result.ExitCode);

        return CommandResponse<VolumeCreateResult>.Ok(new VolumeCreateResult
        {
          Name = result.Output?.Trim() ?? config.Name,
          Driver = config.Driver
        });
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
        DriverContext context, string volumeName, bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var quotedName = QuotePositionalArgument(volumeName, nameof(volumeName));
        var args = force ? $"volume rm -f {quotedName}" : $"volume rm {quotedName}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Volume remove failed"), FailureCode(result.Error, ErrorCodes.Volume.RemoveFailed),
              CreateErrorContext(context, "RemoveVolume", result), result.ExitCode);

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
        DriverContext context, VolumeListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "volume ls --format json";
        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";

          if (filter.Labels != null)
            foreach (var label in filter.Labels)
              args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
        }

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<Volume>>.Fail(
              ErrorOrDefault(result, "Volume list failed"), FailureCode(result.Error, ErrorCodes.General.Unknown),
              CreateErrorContext(context, "ListVolumes", result), result.ExitCode);

        var volumes = ParseVolumeList(result.Output);
        return CommandResponse<IList<Volume>>.Ok(volumes);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Volume>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Volume>> InspectAsync(
        DriverContext context, string volumeName,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context, $"volume inspect {QuotePositionalArgument(volumeName, nameof(volumeName))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Volume>.Fail(
              ErrorOrDefault(result, "Volume inspect failed"), FailureCode(result.Error, ErrorCodes.Volume.InspectFailed),
              CreateErrorContext(context, "InspectVolume", result), result.ExitCode);

        var volume = ParseVolumeInspect(result.Output);
        if (string.IsNullOrEmpty(volume.Name))
          return CommandResponse<Volume>.Fail(
              $"Volume '{volumeName}' was not found", ErrorCodes.Volume.NotFound,
              CreateErrorContext(context, "InspectVolume", result), result.ExitCode);
        return CommandResponse<Volume>.Ok(volume);
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
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, "volume prune -f", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<VolumePruneResult>.Fail(
              ErrorOrDefault(result, "Volume prune failed"), FailureCode(result.Error, ErrorCodes.Volume.PruneFailed),
              CreateErrorContext(context, "PruneVolumes", result), result.ExitCode);

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
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman volume list output: {ex.Message}");
      }

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
      if (string.IsNullOrWhiteSpace(json))
        return new Volume();

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
        throw new FluentDockerException(
            $"Failed to parse Podman volume inspect output: {ex.Message}");
      }
    }

    #endregion
  }
}
