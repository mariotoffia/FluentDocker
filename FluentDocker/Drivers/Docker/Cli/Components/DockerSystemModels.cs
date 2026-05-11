using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using FluentDocker.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>Additional meta keys for Docker-specific system info.</summary>
  public static class DockerSystemInfoMetaKeys
  {
    public const string SwarmActive = "swarmActive";
  }

  /// <summary>
  /// Docker-specific system info payload, mapped from `docker info`.
  /// </summary>
  public class DockerSystemInfo : SystemInfo
  {
    private Dictionary<string, DockerRuntimeInfo> _dockerRuntimes = [];

    [JsonPropertyName("Driver")]
    public string DockerStorageDriver
    {
      get => StorageBackend;
      set => StorageBackend = value;
    }

    [JsonPropertyName("LoggingDriver")]
    public string DockerLoggingDriver
    {
      get => LoggingBackend;
      set => LoggingBackend = value;
    }

    [JsonPropertyName("Name")]
    public string DockerHostname
    {
      get => Hostname;
      set => Hostname = value;
    }

    [JsonPropertyName("MemTotal")]
    public long DockerMemoryTotal
    {
      get => MemoryTotal;
      set => MemoryTotal = value;
    }

    [JsonPropertyName("NCPU")]
    public int DockerCpus
    {
      get => CPUs;
      set => CPUs = value;
    }

    [JsonPropertyName("DockerRootDir")]
    public string DockerDataRoot
    {
      get => DataRoot;
      set => DataRoot = value;
    }

    [JsonPropertyName("ServerVersion")]
    public string DockerEngineVersion
    {
      get => EngineVersion;
      set => EngineVersion = value;
    }

    [JsonPropertyName("Runtimes")]
    public Dictionary<string, DockerRuntimeInfo> DockerRuntimes
    {
      get => _dockerRuntimes;
      set
      {
        _dockerRuntimes = value ?? [];
        Runtimes = _dockerRuntimes.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
      }
    }

    [JsonPropertyName("Swarm")]
    public DockerSwarmInfo Swarm { get; set; }

    [JsonIgnore]
    public bool SwarmActive => Swarm?.LocalNodeState == "active";

    public override void PopulateMeta()
    {
      base.PopulateMeta();
      SetMeta(DockerSystemInfoMetaKeys.SwarmActive, SwarmActive);
    }
  }

  /// <summary>
  /// Docker runtime details.
  /// </summary>
  public class DockerRuntimeInfo
  {
    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("runtimeArgs")]
    public List<string> RuntimeArgs { get; set; } = [];
  }

  /// <summary>
  /// Swarm information from docker info.
  /// </summary>
  public class DockerSwarmInfo
  {
    public string LocalNodeState { get; set; }
    public string NodeID { get; set; }
    public bool ControlAvailable { get; set; }
  }

  /// <summary>
  /// Docker-specific version payload, mapped from `docker version`.
  /// </summary>
  public class DockerVersionInfo : VersionInfo
  {
    [JsonPropertyName("Client")]
    public DockerVersionComponent Client { get; set; }

    [JsonPropertyName("Server")]
    public DockerVersionComponent Server { get; set; }

    /// <summary>
    /// Normalize component values into the generic base properties.
    /// </summary>
    public void PopulateFromComponents()
    {
      ClientVersion ??= Client?.GetVersion();
      ClientApiVersion ??= Client?.GetApiVersion();
      ServerVersion ??= Server?.GetVersion();
      ServerApiVersion ??= Server?.GetApiVersion();
      MinApiVersion ??= Server?.GetMinApiVersion() ?? Client?.GetMinApiVersion();
      GitCommit ??= Server?.GetGitCommit() ?? Client?.GetGitCommit();
      RuntimeVersion ??= Server?.GetGoVersion() ?? Client?.GetGoVersion();
      Os ??= Server?.GetOs() ?? Client?.GetOs();
      Arch ??= Server?.GetArch() ?? Client?.GetArch();
      BuildTime ??= Server?.GetBuildTime() ?? Client?.GetBuildTime();
      PlatformName ??= Server?.Platform?.Name ?? Client?.Platform?.Name;

      if (!Experimental)
      {
        Experimental = Server?.IsExperimental ?? Client?.IsExperimental ?? false;
      }
    }

    public override void PopulateMeta()
    {
      PopulateFromComponents();
      base.PopulateMeta();
    }
  }

  /// <summary>
  /// Version component information (Client or Server) for Docker.
  /// </summary>
  public class DockerVersionComponent
  {
    public string Version { get; set; }
    public string ApiVersion { get; set; }
    public string DefaultAPIVersion { get; set; }
    public string MinAPIVersion { get; set; }
    public string GitCommit { get; set; }
    public string GoVersion { get; set; }
    public string Os { get; set; }
    public string Arch { get; set; }
    public string BuildTime { get; set; }
    public string Experimental { get; set; }
    public string KernelVersion { get; set; }

    [JsonPropertyName("Platform")]
    public DockerVersionPlatform Platform { get; set; }

    [JsonPropertyName("Components")]
    public IList<DockerVersionComponentDetail> Components { get; set; } = [];

    internal bool? IsExperimental => GetExperimentalFlag();

    internal string GetVersion() => GetValueOrDetail(Version, "Version");

    internal string GetApiVersion() => GetValueOrDetail(ApiVersion, "ApiVersion");

    internal string GetMinApiVersion() => GetValueOrDetail(MinAPIVersion, "MinAPIVersion");

    internal string GetGitCommit() => GetValueOrDetail(GitCommit, "GitCommit");

    internal string GetGoVersion() => GetValueOrDetail(GoVersion, "GoVersion");

    internal string GetOs() => GetValueOrDetail(Os, "Os");

    internal string GetArch() => GetValueOrDetail(Arch, "Arch");

    internal string GetBuildTime() => GetValueOrDetail(BuildTime, "BuildTime");

    private string GetValueOrDetail(string primary, string detailKey)
    {
      if (!string.IsNullOrEmpty(primary))
        return primary;

      return GetDetailValue(detailKey);
    }

    private bool? GetExperimentalFlag()
    {
      if (bool.TryParse(Experimental, out var parsed))
        return parsed;

      var detail = GetDetailValue("Experimental");
      if (bool.TryParse(detail, out var detailParsed))
        return detailParsed;

      return null;
    }

    private string GetDetailValue(string key)
    {
      if (Components == null)
        return null;

      foreach (var component in Components)
      {
        if (!string.IsNullOrEmpty(component.Version) &&
            string.Equals(key, "Version", System.StringComparison.OrdinalIgnoreCase))
        {
          return component.Version;
        }

        if (component.Details == null)
          continue;

        foreach (var detail in component.Details)
        {
          if (string.Equals(detail.Key, key, System.StringComparison.OrdinalIgnoreCase))
            return detail.Value?.ToString();
        }
      }

      return null;
    }
  }

  public class DockerVersionComponentDetail
  {
    public string Name { get; set; }
    public string Version { get; set; }
    public Dictionary<string, object> Details { get; set; } = [];
  }

  public class DockerVersionPlatform
  {
    public string Name { get; set; }
  }
}
