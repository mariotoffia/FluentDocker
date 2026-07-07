using System;
using System.Globalization;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  public partial class PodmanCliSystemDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    #region JSON Parsing

    private static SystemInfo ParseSystemInfo(string json)
    {
      var info = new SystemInfo();
      if (string.IsNullOrWhiteSpace(json))
        return info;

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

        var security = host?.Prop("security");
        if (security.HasValue)
          info.Rootless = security.Value.GetBoolOrDefault("rootless");

        info.OSType = "linux"; // Podman always runs Linux containers
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman system info output: {ex.Message}");
      }

      return info;
    }

    private static VersionInfo ParseVersionInfo(string json)
    {
      var version = new VersionInfo();
      if (string.IsNullOrWhiteSpace(json))
        return version;

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
        version.BuildTime = client.GetStringOrDefault("BuiltTime");

        var server = obj.Prop("Server");
        if (server.HasValue)
        {
          var s = server.Value;
          version.ServerVersion = s.GetStringOrDefault("Version");
          version.ServerApiVersion = s.GetStringOrDefault("APIVersion");
        }
        else
        {
          // Server is present only for remote connections, not as a rootless/rootful signal.
          version.ServerVersion = version.ClientVersion;
          version.ServerApiVersion = version.ClientApiVersion;
        }

        version.PlatformName = "Podman";
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman system version output: {ex.Message}");
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

      try
      {
        var trimmed = output.Trim();
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
          {
            if (token.ValueKind == JsonValueKind.Object)
              ApplyDiskUsageItem(info, token);
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
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman system disk usage output: {ex.Message}");
      }
    }

    private static void ParseLineByLine(DiskUsageInfo info, string output)
    {
      var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines)
      {
        var l = line.Trim();
        if (l.Length == 0)
          continue;
        if (l[0] != '{')
          throw new FormatException("expected a JSON object line");

        var obj = JsonHelper.ParseElement(l);
        ApplyDiskUsageItem(info, obj);
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
      if (!prop.HasValue)
        return 0;
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
      if (!prop.HasValue)
        return 0;
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
