using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of ISystemDriver.
  /// Uses GET /info, GET /version, GET /_ping, GET /system/df, and per-resource prune.
  /// </summary>
  public class DockerApiSystemDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection), ISystemDriver
  {
    public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync("/info", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<SystemInfo>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /info", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var info = ParseSystemInfo(result.Data);
      return CommandResponse<SystemInfo>.Ok(info);
    }

    public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync("/version", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<VersionInfo>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /version", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var version = ParseVersionInfo(result.Data);
      return CommandResponse<VersionInfo>.Ok(version);
    }

    public async Task<CommandResponse<Unit>> PingAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var ok = await Connection.PingAsync(cancellationToken).ConfigureAwait(false);
      return ok
          ? CommandResponse<Unit>.Ok(Unit.Default)
          : CommandResponse<Unit>.Fail("Docker daemon is not responding",
              ErrorCodes.Driver.NotAvailable);
    }

    public async Task<CommandResponse<bool>> IsWindowsEngineAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var infoResult = await GetInfoAsync(context, cancellationToken).ConfigureAwait(false);
      if (!infoResult.Success)
        return CommandResponse<bool>.Fail(infoResult.Error, infoResult.ErrorCode);
      return CommandResponse<bool>.Ok(
          infoResult.Data.OSType?.ToLowerInvariant() == "windows");
    }

    public async Task<CommandResponse<bool>> IsLinuxEngineAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var infoResult = await GetInfoAsync(context, cancellationToken).ConfigureAwait(false);
      if (!infoResult.Success)
        return CommandResponse<bool>.Fail(infoResult.Error, infoResult.ErrorCode);
      return CommandResponse<bool>.Ok(
          infoResult.Data.OSType?.ToLowerInvariant() == "linux");
    }

    public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync("/system/df", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<DiskUsageInfo>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /system/df", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var usage = ParseDiskUsageInfo(result.Data);
      return CommandResponse<DiskUsageInfo>.Ok(usage);
    }

    public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
        DriverContext context, SystemPruneConfig config = null,
        CancellationToken cancellationToken = default)
    {
      config ??= new SystemPruneConfig();
      var pruneResult = new SystemPruneResult();
      var filterQuery = BuildFilterQuery(config.Filter);

      // 1. Prune containers
      var ctrResult = await PostJsonElementAsync(
          $"/containers/prune{filterQuery}", null, cancellationToken);
      if (ctrResult.Success && ctrResult.Data.ValueKind == JsonValueKind.Object)
      {
        var deletedEl = ctrResult.Data.Prop("ContainersDeleted");
        if (deletedEl?.ValueKind == JsonValueKind.Array)
        {
          var deleted = deletedEl.Value.Deserialize<List<string>>();
          if (deleted != null)
            pruneResult.ContainersDeleted.AddRange(deleted);
        }
        pruneResult.SpaceReclaimed += ctrResult.Data.GetInt64OrDefault("SpaceReclaimed");
      }

      // 2. Prune networks
      var netResult = await PostJsonElementAsync(
          $"/networks/prune{filterQuery}", null, cancellationToken);
      if (netResult.Success && netResult.Data.ValueKind == JsonValueKind.Object)
      {
        var deletedEl = netResult.Data.Prop("NetworksDeleted");
        if (deletedEl?.ValueKind == JsonValueKind.Array)
        {
          var deleted = deletedEl.Value.Deserialize<List<string>>();
          if (deleted != null)
            pruneResult.NetworksDeleted.AddRange(deleted);
        }
      }

      // 3. Prune images (All -> dangling=false to prune all unused images)
      var imageFilterQuery = BuildImagePruneFilterQuery(config);
      var imgResult = await PostJsonElementAsync(
          $"/images/prune{imageFilterQuery}", null, cancellationToken);
      if (imgResult.Success && imgResult.Data.ValueKind == JsonValueKind.Object)
      {
        var deletedEl = imgResult.Data.Prop("ImagesDeleted");
        if (deletedEl?.ValueKind == JsonValueKind.Array)
        {
          var deleted = deletedEl.Value.EnumerateArray()
              .Select(t => t.GetStringOrDefault("Untagged") ?? t.GetStringOrDefault("Deleted"))
              .Where(s => s != null).ToList();
          pruneResult.ImagesDeleted.AddRange(deleted);
        }
        pruneResult.SpaceReclaimed += imgResult.Data.GetInt64OrDefault("SpaceReclaimed");
      }

      // 4. Prune volumes (only when opted-in, matching docker system prune)
      if (config.Volumes)
      {
        var volResult = await PostJsonElementAsync(
            $"/volumes/prune{filterQuery}", null, cancellationToken);
        if (volResult.Success && volResult.Data.ValueKind == JsonValueKind.Object)
        {
          var deletedEl = volResult.Data.Prop("VolumesDeleted");
          if (deletedEl?.ValueKind == JsonValueKind.Array)
          {
            var deleted = deletedEl.Value.Deserialize<List<string>>();
            if (deleted != null)
              pruneResult.VolumesDeleted.AddRange(deleted);
          }
          pruneResult.SpaceReclaimed += volResult.Data.GetInt64OrDefault("SpaceReclaimed");
        }
      }

      // 5. Prune build cache
      var buildResult = await PostJsonElementAsync(
          "/build/prune", null, cancellationToken);
      if (buildResult.Success && buildResult.Data.ValueKind == JsonValueKind.Object)
      {
        var cachesEl = buildResult.Data.Prop("CachesDeleted");
        if (cachesEl?.ValueKind == JsonValueKind.Array)
        {
          var cacheIds = cachesEl.Value.Deserialize<List<string>>();
          if (cacheIds != null)
            pruneResult.BuildCacheDeleted.AddRange(cacheIds);
        }
        pruneResult.SpaceReclaimed += buildResult.Data.GetInt64OrDefault("SpaceReclaimed");
      }

      return CommandResponse<SystemPruneResult>.Ok(pruneResult);
    }

    /// <summary>
    /// Encodes a filter dictionary as a <c>?filters={"key":["value"]}</c> query param.
    /// </summary>
    private static string BuildFilterQuery(Dictionary<string, string> filter)
    {
      if (filter == null || filter.Count == 0)
        return string.Empty;

      var dict = filter.ToDictionary(
          kv => kv.Key,
          kv => new List<string> { kv.Value });
      return $"?filters={Uri.EscapeDataString(JsonHelper.Serialize(dict))}";
    }

    /// <summary>
    /// Builds the filter query for image prune, combining <c>All</c> flag
    /// (as <c>dangling=false</c>) with user-provided filters.
    /// </summary>
    private static string BuildImagePruneFilterQuery(SystemPruneConfig config)
    {
      var dict = new Dictionary<string, List<string>>();
      if (config.All)
        dict["dangling"] = ["false"];
      if (config.Filter != null)
        foreach (var kv in config.Filter)
          dict[kv.Key] = [kv.Value];

      return dict.Count == 0
          ? string.Empty
          : $"?filters={Uri.EscapeDataString(JsonHelper.Serialize(dict))}";
    }

    public Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Daemon switching is not supported via the Docker API",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    public Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Daemon switching is not supported via the Docker API",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    public Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Daemon switching is not supported via the Docker API",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    #region JSON Parsing

    private static SystemInfo ParseSystemInfo(JsonElement json)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new SystemInfo();

      var info = new SystemInfo
      {
        OperatingSystem = json.GetStringOrDefault("OperatingSystem"),
        OSType = json.GetStringOrDefault("OSType"),
        OSVersion = json.GetStringOrDefault("OSVersion"),
        Architecture = json.GetStringOrDefault("Architecture"),
        Containers = json.GetInt32OrDefault("Containers"),
        ContainersRunning = json.GetInt32OrDefault("ContainersRunning"),
        ContainersPaused = json.GetInt32OrDefault("ContainersPaused"),
        ContainersStopped = json.GetInt32OrDefault("ContainersStopped"),
        Images = json.GetInt32OrDefault("Images"),
        EngineVersion = json.GetStringOrDefault("ServerVersion"),
        StorageBackend = json.GetStringOrDefault("Driver"),
        LoggingBackend = json.GetStringOrDefault("LoggingDriver"),
        KernelVersion = json.GetStringOrDefault("KernelVersion"),
        MemoryTotal = json.GetInt64OrDefault("MemTotal"),
        CPUs = json.GetInt32OrDefault("NCPU"),
        DataRoot = json.GetStringOrDefault("DockerRootDir"),
        Hostname = json.GetStringOrDefault("Name"),
        DefaultRuntime = json.GetStringOrDefault("DefaultRuntime"),
      };

      info.PopulateMeta();
      return info;
    }

    private static VersionInfo ParseVersionInfo(JsonElement json)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new VersionInfo();

      var version = new VersionInfo
      {
        ServerVersion = json.GetStringOrDefault("Version"),
        ServerApiVersion = json.GetStringOrDefault("ApiVersion"),
        MinApiVersion = json.GetStringOrDefault("MinAPIVersion"),
        GitCommit = json.GetStringOrDefault("GitCommit"),
        RuntimeVersion = json.GetStringOrDefault("GoVersion"),
        Os = json.GetStringOrDefault("Os"),
        Arch = json.GetStringOrDefault("Arch"),
        BuildTime = json.GetStringOrDefault("BuildTime"),
        Experimental = json.GetBoolOrDefault("Experimental"),
      };

      var platform = json.Prop("Platform");
      if (platform?.ValueKind == JsonValueKind.Object)
        version.PlatformName = platform.Value.GetStringOrDefault("Name");

      // For API driver, client version = server version (direct API access)
      version.ClientVersion = version.ServerVersion;
      version.ClientApiVersion = version.ServerApiVersion;

      version.PopulateMeta();
      return version;
    }

    private static DiskUsageInfo ParseDiskUsageInfo(JsonElement json)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new DiskUsageInfo();

      var usage = new DiskUsageInfo();
      long totalSize = 0;

      var imagesEl = json.Prop("Images");
      if (imagesEl?.ValueKind == JsonValueKind.Array)
      {
        var images = imagesEl.Value;
        usage.Images.TotalCount = images.GetArrayLength();
        foreach (var img in images.EnumerateArray())
        {
          var size = img.GetInt64OrDefault("Size");
          usage.Images.Size += size;
          totalSize += size;
        }
      }

      var containersEl = json.Prop("Containers");
      if (containersEl?.ValueKind == JsonValueKind.Array)
      {
        var containers = containersEl.Value;
        usage.Containers.TotalCount = containers.GetArrayLength();
        foreach (var c in containers.EnumerateArray())
        {
          var size = c.GetInt64OrDefault("SizeRw");
          usage.Containers.Size += size;
          totalSize += size;
        }
      }

      var volumesEl = json.Prop("Volumes");
      if (volumesEl?.ValueKind == JsonValueKind.Array)
      {
        var volumes = volumesEl.Value;
        usage.Volumes.TotalCount = volumes.GetArrayLength();
        foreach (var v in volumes.EnumerateArray())
        {
          var usageData = v.Prop("UsageData");
          var size = usageData?.GetInt64OrDefault("Size") ?? 0;
          usage.Volumes.Size += size;
          totalSize += size;
        }
      }

      usage.TotalSize = totalSize;
      return usage;
    }

    #endregion
  }
}
