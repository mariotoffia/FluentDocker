using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using FluentDocker.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>Additional meta keys for Docker-specific system info.</summary>
  public static class DockerSystemInfoMetaKeys
  {
    /// <summary>Whether the daemon reports an active Swarm mode (<see cref="DockerSystemInfo.SwarmActive"/>).</summary>
    public const string SwarmActive = "swarmActive";
  }

  /// <summary>
  /// Docker-specific system info payload, mapped from `docker info`.
  /// </summary>
  public class DockerSystemInfo : SystemInfo
  {
    private Dictionary<string, DockerRuntimeInfo> _dockerRuntimes = [];

    /// <summary>Docker's <c>Driver</c> field (storage driver); mirrors <see cref="SystemInfo.StorageBackend"/>.</summary>
    [JsonPropertyName("Driver")]
    public string? DockerStorageDriver
    {
      get => StorageBackend;
      set => StorageBackend = value;
    }

    /// <summary>Docker's <c>LoggingDriver</c> field; mirrors <see cref="SystemInfo.LoggingBackend"/>.</summary>
    [JsonPropertyName("LoggingDriver")]
    public string? DockerLoggingDriver
    {
      get => LoggingBackend;
      set => LoggingBackend = value;
    }

    /// <summary>Docker's <c>Name</c> field (daemon host name); mirrors <see cref="SystemInfo.Hostname"/>.</summary>
    [JsonPropertyName("Name")]
    public string? DockerHostname
    {
      get => Hostname;
      set => Hostname = value;
    }

    /// <summary>Docker's <c>MemTotal</c> field, in bytes; mirrors <see cref="SystemInfo.MemoryTotal"/>.</summary>
    [JsonPropertyName("MemTotal")]
    public long DockerMemoryTotal
    {
      get => MemoryTotal;
      set => MemoryTotal = value;
    }

    /// <summary>Docker's <c>NCPU</c> field; mirrors <see cref="SystemInfo.CPUs"/>.</summary>
    [JsonPropertyName("NCPU")]
    public int DockerCpus
    {
      get => CPUs;
      set => CPUs = value;
    }

    /// <summary>Docker's <c>DockerRootDir</c> field; mirrors <see cref="SystemInfo.DataRoot"/>.</summary>
    [JsonPropertyName("DockerRootDir")]
    public string? DockerDataRoot
    {
      get => DataRoot;
      set => DataRoot = value;
    }

    /// <summary>
    /// Docker engine version; not deserialized directly (populated from `docker version` via
    /// <see cref="DockerVersionInfo.PopulateFromComponents"/>). Mirrors <see cref="SystemInfo.EngineVersion"/>.
    /// </summary>
    [JsonIgnore]
    public string? DockerEngineVersion
    {
      get => EngineVersion;
      set => EngineVersion = value;
    }

    /// <summary>
    /// Docker's <c>Runtimes</c> field (OCI runtime name to configuration). Setting this also
    /// repopulates the base <see cref="SystemInfo.Runtimes"/> dictionary used cross-driver.
    /// </summary>
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

    /// <summary>Docker's <c>Swarm</c> field: Swarm mode status reported by the daemon.</summary>
    [JsonPropertyName("Swarm")]
    public DockerSwarmInfo? Swarm { get; set; }

    /// <summary>True when <see cref="Swarm"/> reports <c>LocalNodeState == "active"</c>.</summary>
    [JsonIgnore]
    public bool SwarmActive => Swarm?.LocalNodeState == "active";

    /// <inheritdoc />
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
    /// <summary>Path to the OCI runtime binary (Docker's <c>path</c> field).</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>Extra arguments passed to the runtime binary (Docker's <c>runtimeArgs</c> field).</summary>
    [JsonPropertyName("runtimeArgs")]
    public List<string> RuntimeArgs { get; set; } = [];
  }

  /// <summary>
  /// Swarm information from docker info.
  /// </summary>
  public class DockerSwarmInfo
  {
    /// <summary>Swarm membership state (e.g. <c>"inactive"</c>, <c>"active"</c>, <c>"pending"</c>).</summary>
    public string? LocalNodeState { get; set; }

    /// <summary>ID of this node within the Swarm, empty when not a member.</summary>
    public string? NodeID { get; set; }

    /// <summary>True if this node has manager/control-plane availability.</summary>
    public bool ControlAvailable { get; set; }
  }

  /// <summary>
  /// Docker-specific version payload, mapped from `docker version`.
  /// </summary>
  public class DockerVersionInfo : VersionInfo
  {
    /// <summary>Docker's <c>Client</c> field: version details for the CLI client.</summary>
    [JsonPropertyName("Client")]
    public DockerVersionComponent? Client { get; set; }

    /// <summary>Docker's <c>Server</c> field: version details for the daemon/engine.</summary>
    [JsonPropertyName("Server")]
    public DockerVersionComponent? Server { get; set; }

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

    /// <inheritdoc />
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
    /// <summary>Component version string; falls back to the matching entry in <see cref="Components"/> when blank.</summary>
    public string? Version { get; set; }

    /// <summary>Docker Engine API version negotiated by this component.</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Default API version this component was built with.</summary>
    public string? DefaultAPIVersion { get; set; }

    /// <summary>Minimum API version this component supports.</summary>
    public string? MinAPIVersion { get; set; }

    /// <summary>Git commit hash this component was built from.</summary>
    public string? GitCommit { get; set; }

    /// <summary>Go toolchain version this component was built with.</summary>
    public string? GoVersion { get; set; }

    /// <summary>Operating system this component targets (e.g. <c>linux</c>).</summary>
    public string? Os { get; set; }

    /// <summary>CPU architecture this component targets (e.g. <c>amd64</c>).</summary>
    public string? Arch { get; set; }

    /// <summary>Build timestamp of this component.</summary>
    public string? BuildTime { get; set; }

    /// <summary>
    /// Raw experimental-features flag as reported by the daemon (string, not bool: some
    /// engine versions omit or vary its shape). Use <see cref="IsExperimental"/> to read it
    /// as a parsed <see cref="bool"/> with the <see cref="Components"/> fallback applied.
    /// </summary>
    public string? Experimental { get; set; }

    /// <summary>Host kernel version reported alongside this component.</summary>
    public string? KernelVersion { get; set; }

    /// <summary>Build platform metadata for this component.</summary>
    [JsonPropertyName("Platform")]
    public DockerVersionPlatform? Platform { get; set; }

    /// <summary>
    /// Detailed sub-component list some engine versions report instead of top-level fields;
    /// consulted as a fallback by the <c>Get*</c> accessors when the primary property is blank.
    /// </summary>
    [JsonPropertyName("Components")]
    public IList<DockerVersionComponentDetail> Components { get; set; } = [];

    internal bool? IsExperimental => GetExperimentalFlag();

    internal string? GetVersion() => GetValueOrDetail(Version, "Version");

    internal string? GetApiVersion() => GetValueOrDetail(ApiVersion, "ApiVersion");

    internal string? GetMinApiVersion() => GetValueOrDetail(MinAPIVersion, "MinAPIVersion");

    internal string? GetGitCommit() => GetValueOrDetail(GitCommit, "GitCommit");

    internal string? GetGoVersion() => GetValueOrDetail(GoVersion, "GoVersion");

    internal string? GetOs() => GetValueOrDetail(Os, "Os");

    internal string? GetArch() => GetValueOrDetail(Arch, "Arch");

    internal string? GetBuildTime() => GetValueOrDetail(BuildTime, "BuildTime");

    private string? GetValueOrDetail(string? primary, string detailKey)
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

    private string? GetDetailValue(string key)
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

  /// <summary>A single sub-component entry from Docker's <c>Components</c> version list.</summary>
  public class DockerVersionComponentDetail
  {
    /// <summary>Sub-component name (e.g. <c>"Engine"</c>, <c>"containerd"</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Sub-component version string.</summary>
    public string? Version { get; set; }

    /// <summary>Extra key/value details reported for this sub-component (e.g. <c>ApiVersion</c>, <c>Experimental</c>).</summary>
    public Dictionary<string, object> Details { get; set; } = [];
  }

  /// <summary>Build platform metadata reported alongside a <see cref="DockerVersionComponent"/>.</summary>
  public class DockerVersionPlatform
  {
    /// <summary>Platform/distribution name (e.g. <c>"Docker Engine - Community"</c>).</summary>
    public string? Name { get; set; }
  }
}
