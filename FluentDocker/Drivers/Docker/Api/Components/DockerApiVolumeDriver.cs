using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IVolumeDriver.
  /// Uses /volumes endpoints.
  /// </summary>
  public class DockerApiVolumeDriver : DockerApiDriverBase, IVolumeDriver
  {
    public DockerApiVolumeDriver(IDockerApiConnection connection) : base(connection) { }

    public async Task<CommandResponse<VolumeCreateResult>> CreateAsync(
        DriverContext context, VolumeCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var body = new
      {
        Name = config.Name,
        Driver = config.Driver ?? "local",
        DriverOpts = config.DriverOpts?.Count > 0 ? config.DriverOpts : null,
        Labels = config.Labels?.Count > 0 ? config.Labels : null
      };

      var result = await PostJsonElementAsync("/volumes/create", body, cancellationToken);
      if (!result.Success)
        return CommandResponse<VolumeCreateResult>.Fail(result.ErrorMessage,
            ErrorCodes.Volume.CreateFailed,
            CreateErrorContext("POST /volumes/create", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<VolumeCreateResult>.Ok(new VolumeCreateResult
      {
        Name = result.Data.GetStringOrDefault("Name") ?? config.Name,
        Driver = result.Data.GetStringOrDefault("Driver") ?? config.Driver
      });
    }

    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string volumeName, bool force = false,
        CancellationToken cancellationToken = default)
    {
      var path = $"/volumes/{volumeName}?force={force.ToString().ToLower()}";
      var result = await DeleteAsync(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Volume.NotFound),
            CreateErrorContext($"DELETE /volumes/{volumeName}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    public async Task<CommandResponse<IList<Volume>>> ListAsync(
        DriverContext context, VolumeListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      var path = "/volumes";
      if (filter?.Name != null)
      {
        var filters = $"{{\"name\":[\"{filter.Name}\"]}}";
        path += $"?filters={System.Uri.EscapeDataString(filters)}";
      }

      var result = await GetJsonElementAsync(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<IList<Volume>>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /volumes", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var volumes = new List<Volume>();
      var arr = result.Data.Prop("Volumes");
      if (arr?.ValueKind == JsonValueKind.Array)
      {
        volumes.AddRange(arr.Value.EnumerateArray().Select(ParseVolume));
      }

      return CommandResponse<IList<Volume>>.Ok(volumes);
    }

    public async Task<CommandResponse<Volume>> InspectAsync(
        DriverContext context, string volumeName,
        CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync($"/volumes/{volumeName}", cancellationToken);
      if (!result.Success)
        return CommandResponse<Volume>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Volume.NotFound),
            CreateErrorContext($"GET /volumes/{volumeName}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Volume>.Ok(ParseVolume(result.Data));
    }

    public async Task<CommandResponse<VolumePruneResult>> PruneAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var result = await PostJsonElementAsync("/volumes/prune", null, cancellationToken);
      if (!result.Success)
        return CommandResponse<VolumePruneResult>.Fail(result.ErrorMessage,
            ErrorCodes.Volume.PruneFailed,
            CreateErrorContext("POST /volumes/prune", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var pruneResult = new VolumePruneResult
      {
        SpaceReclaimed = result.Data.GetInt64OrDefault("SpaceReclaimed")
      };

      var deleted = result.Data.Prop("VolumesDeleted");
      if (deleted?.ValueKind == JsonValueKind.Array)
      {
        pruneResult.VolumesDeleted = deleted.Value.EnumerateArray()
            .Select(v => v.GetString()).ToList();
      }

      return CommandResponse<VolumePruneResult>.Ok(pruneResult);
    }

    private static Volume ParseVolume(JsonElement token)
    {
      if (token.ValueKind != JsonValueKind.Object)
        return new Volume();
      var volume = new Volume
      {
        Name = token.GetStringOrDefault("Name"),
        Driver = token.GetStringOrDefault("Driver"),
        Created = token.GetDateTimeOrDefault("CreatedAt"),
        Scope = token.GetStringOrDefault("Scope"),
        Mountpoint = token.GetStringOrDefault("Mountpoint")
      };
      var labels = token.Prop("Labels");
      if (labels?.ValueKind == JsonValueKind.Object)
        volume.Labels = labels.Value.Deserialize<Dictionary<string, string>>();
      var options = token.Prop("Options");
      if (options?.ValueKind == JsonValueKind.Object)
        volume.Options = options.Value.Deserialize<Dictionary<string, string>>();
      return volume;
    }
  }
}
