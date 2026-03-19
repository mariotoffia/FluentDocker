using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of ISystemDriver.
  /// Adapted for Podman's daemonless architecture.
  /// </summary>
  public class PodmanCliSystemDriver : PodmanCliDriverBase, ISystemDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    public PodmanCliSystemDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("info --format json", cancellationToken);
        if (!result.Success)
          return CommandResponse<SystemInfo>.Fail(
              result.Error ?? "System info failed", ErrorCodes.General.Unknown);

        var info = ParseSystemInfo(result.Output);
        info.PopulateMeta();
        return CommandResponse<SystemInfo>.Ok(info);
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("version --format json", cancellationToken);
        if (!result.Success)
          return CommandResponse<VersionInfo>.Fail(
              result.Error ?? "Version check failed", ErrorCodes.General.Unknown);

        var version = ParseVersionInfo(result.Output);
        version.PopulateMeta();
        return CommandResponse<VersionInfo>.Ok(version);
      }
      catch (Exception ex)
      {
        return CommandResponse<VersionInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PingAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        // Podman is daemonless; verify it works by running 'podman info'
        var result = await ExecuteCommandAsync("info", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail("Podman is not reachable", ErrorCodes.General.Unknown);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public Task<CommandResponse<bool>> IsWindowsEngineAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      // Podman always uses Linux containers (even on macOS/Windows via VM)
      return Task.FromResult(CommandResponse<bool>.Ok(false));
    }

    /// <inheritdoc />
    public Task<CommandResponse<bool>> IsLinuxEngineAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      // Podman always uses Linux containers
      return Task.FromResult(CommandResponse<bool>.Ok(true));
    }

    #endregion

    #region Maintenance Operations

    /// <inheritdoc />
    public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync("system df --format json", cancellationToken);
        if (!result.Success)
          return CommandResponse<DiskUsageInfo>.Fail(
              result.Error ?? "Disk usage failed", ErrorCodes.General.Unknown);

        var info = ParseDiskUsageOutput(result.Output);
        return CommandResponse<DiskUsageInfo>.Ok(info);
      }
      catch (Exception ex)
      {
        return CommandResponse<DiskUsageInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
        DriverContext context, SystemPruneConfig config = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildSystemPruneArgs(config);

        var result = await ExecuteCommandAsync(args, cancellationToken);
        if (!result.Success)
          return CommandResponse<SystemPruneResult>.Fail(
              result.Error ?? "System prune failed", ErrorCodes.General.Unknown);

        return CommandResponse<SystemPruneResult>.Ok(
            CliPruneOutputParser.ParseSystemPruneOutput(result.Output));
      }
      catch (Exception ex)
      {
        return CommandResponse<SystemPruneResult>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    #endregion

    #region Daemon Operations (Not applicable to Podman)

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Podman is daemonless and does not support daemon switching",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Podman is daemonless and always runs Linux containers",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    /// <inheritdoc />
    public Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      // Podman does not support Windows containers
      return Task.FromResult(CommandResponse<Unit>.Fail(
          "Podman does not support Windows containers",
          ErrorCodes.Driver.CapabilityNotSupported));
    }

    #endregion

    #region Argument Building

    /// <summary>
    /// Builds the CLI arguments string for <c>podman system prune</c>.
    /// </summary>
    public static string BuildSystemPruneArgs(SystemPruneConfig config)
    {
      var args = "system prune -f";
      if (config?.All == true)
        args += " -a";
      if (config?.Volumes == true)
        args += " --volumes";
      if (config?.Filter != null)
      {
        foreach (var f in config.Filter)
          args += $" --filter {f.Key}={f.Value}";
      }
      return args;
    }

    #endregion

    #region JSON Parsing

    private static SystemInfo ParseSystemInfo(string json)
    {
      var info = new SystemInfo();
      try
      {
        var obj = JsonHelper.ParseElement(json);

        // Podman info structure: { host: {...}, store: {...}, ... }
        var host = obj.Prop("host");
        if (host.HasValue)
        {
          var h = host.Value;
          info.OperatingSystem = h.GetStringOrDefault("os");
          info.Architecture = h.GetStringOrDefault("arch");
          info.Hostname = h.GetStringOrDefault("hostname");
          info.KernelVersion = h.GetStringOrDefault("kernel");
          info.CPUs = h.GetInt32OrDefault("cpus");
          info.MemoryTotal = h.GetInt64OrDefault("memTotal");

          var conmon = h.Prop("conmon");
          if (conmon.HasValue)
            info.EngineVersion = conmon.Value.GetStringOrDefault("version");
        }

        var store = obj.Prop("store");
        if (store.HasValue)
        {
          var s = store.Value;
          info.StorageBackend = s.GetStringOrDefault("graphDriverName");
          info.DataRoot = s.GetStringOrDefault("graphRoot");
          var imgStore = s.Prop("imageStore");
          if (imgStore.HasValue)
            info.Images = imgStore.Value.GetInt32OrDefault("number");
        }

        // Podman version info
        var version = obj.Prop("version");
        if (version.HasValue)
          info.EngineVersion = version.Value.GetStringOrDefault("Version") ?? info.EngineVersion;

        info.OSType = "linux"; // Podman always runs Linux containers
      }
      catch (Exception ex)
      {
        Logger.Log($"Podman system info parsing failed: {ex.Message}");
      }

      return info;
    }

    private static VersionInfo ParseVersionInfo(string json)
    {
      var version = new VersionInfo();
      try
      {
        var obj = JsonHelper.ParseElement(json);

        // Podman version JSON: { Client: {...}, Server: {...} } or flat structure
        var clientProp = obj.Prop("Client");
        var client = clientProp ?? obj;
        version.ClientVersion = client.GetStringOrDefault("Version");
        version.ClientApiVersion = client.GetStringOrDefault("APIVersion");
        version.GitCommit = client.GetStringOrDefault("GitCommit");
        version.RuntimeVersion = client.GetStringOrDefault("GoVersion");
        version.Os = client.GetStringOrDefault("Os") ?? client.GetStringOrDefault("OsArch");
        version.Arch = client.GetStringOrDefault("Arch");
        version.BuildTime = client.GetStringOrDefault("Built");

        var server = obj.Prop("Server");
        if (server.HasValue)
        {
          var s = server.Value;
          version.ServerVersion = s.GetStringOrDefault("Version");
          version.ServerApiVersion = s.GetStringOrDefault("APIVersion");
        }
        else
        {
          // In rootless mode, Server may not be present
          version.ServerVersion = version.ClientVersion;
          version.ServerApiVersion = version.ClientApiVersion;
        }

        version.PlatformName = "Podman";
      }
      catch (Exception ex)
      {
        Logger.Log($"Podman version info parsing failed: {ex.Message}");
      }

      return version;
    }

    #endregion

    #region Disk Usage Parsing

    /// <summary>
    /// Parses Podman CLI <c>system df --format json</c> output.
    /// Handles both JSON arrays and newline-delimited JSON objects.
    /// Size/Reclaimable may be numbers (bytes) or human-readable strings.
    /// </summary>
    public static DiskUsageInfo ParseDiskUsageOutput(string output)
    {
      var info = new DiskUsageInfo();
      if (string.IsNullOrWhiteSpace(output))
        return info;

      var trimmed = output.Trim();

      // Try parsing as a JSON array first
      if (trimmed.StartsWith('['))
      {
        try
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
          {
            if (token.ValueKind == JsonValueKind.Object)
              ApplyDiskUsageItem(info, token);
          }
        }
        catch (Exception ex)
        {
          Logger.Log($"Podman disk usage JSON parsing failed: {ex.Message}");
          ParseLineByLine(info, trimmed);
        }
      }
      else
      {
        ParseLineByLine(info, trimmed);
      }

      info.TotalSize = info.Images.Size + info.Containers.Size
                       + info.Volumes.Size + info.BuildCache.Size;
      info.Reclaimable = info.Images.Reclaimable + info.Containers.Reclaimable
                         + info.Volumes.Reclaimable + info.BuildCache.Reclaimable;
      return info;
    }

    private static void ParseLineByLine(DiskUsageInfo info, string output)
    {
      var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines)
      {
        var l = line.Trim();
        if (l.Length == 0 || l[0] != '{')
          continue;

        try
        {
          var obj = JsonHelper.ParseElement(l);
          ApplyDiskUsageItem(info, obj);
        }
        catch (Exception ex)
        {
          Logger.Log($"Podman disk usage line parsing failed: {ex.Message}");
        }
      }
    }

    private static void ApplyDiskUsageItem(DiskUsageInfo info, JsonElement obj)
    {
      var type = obj.GetStringOrDefault("Type") ?? obj.GetStringOrDefault("type") ?? "";
      var item = new DiskUsageItem
      {
        TotalCount = ReadInt(obj, "Total", "TotalCount"),
        Active = ReadInt(obj, "Active", "active"),
        Size = ReadByteValue(obj, "Size", "size"),
        Reclaimable = ReadByteValue(obj, "Reclaimable", "reclaimable")
      };

      switch (type)
      {
        case "Images":
          info.Images = item;
          break;
        case "Containers":
          info.Containers = item;
          break;
        case "Volumes":
        case "Local Volumes":
          info.Volumes = item;
          break;
        case "Build Cache":
          info.BuildCache = item;
          break;
      }
    }

    private static int ReadInt(JsonElement obj, string key1, string key2)
    {
      var prop = obj.Prop(key1, key2);
      if (!prop.HasValue) return 0;
      var p = prop.Value;
      if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
        return v;
      if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out v))
        return v;
      return 0;
    }

    /// <summary>
    /// Reads a byte value from a JsonElement property. The value may be a number
    /// (raw bytes) or a human-readable string (e.g. "500MB", "1.5GB (40%)").
    /// </summary>
    private static long ReadByteValue(JsonElement obj, string key1, string key2)
    {
      var prop = obj.Prop(key1, key2);
      if (!prop.HasValue) return 0;
      var p = prop.Value;

      if (p.ValueKind == JsonValueKind.Number)
        return p.TryGetInt64(out var lv) ? lv : 0;

      var str = p.GetStringValue();
      if (string.IsNullOrWhiteSpace(str))
        return 0;

      // Strip trailing " (N%)" if present (reclaimable field)
      var parenIndex = str.IndexOf('(');
      if (parenIndex >= 0)
        str = str.Substring(0, parenIndex).Trim();

      // Try raw number first
      if (long.TryParse(str, NumberStyles.Integer,
              CultureInfo.InvariantCulture, out var rawLong))
        return rawLong;

      return PodmanCliContainerDriver.ParseByteValue(str);
    }

    #endregion
  }
}
