using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// System-level driver operations (info, version, ping, etc.).
  /// Supported by: Docker, Podman
  /// </summary>
  public interface ISystemDriver
  {
    #region Information Operations

    /// <summary>
    /// Gets system information.
    /// </summary>
    Task<CommandResponse<SystemInfo>> GetInfoAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets version information.
    /// </summary>
    Task<CommandResponse<VersionInfo>> GetVersionAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pings the daemon to check if it's responsive.
    /// </summary>
    Task<CommandResponse<Unit>> PingAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the engine is Windows-based.
    /// </summary>
    Task<CommandResponse<bool>> IsWindowsEngineAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the engine is Linux-based. Probe failures are returned as failed
    /// <see cref="CommandResponse{T}"/> values; callers must not treat daemon-down as Linux.
    /// </summary>
    Task<CommandResponse<bool>> IsLinuxEngineAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    #endregion

    #region Maintenance Operations

    /// <summary>
    /// Gets disk usage information.
    /// </summary>
    Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes unused resources (containers, networks, images, volumes).
    /// </summary>
    /// <remarks>
    /// On partial failure the call fails atomically — resources pruned before the failure
    /// are not returned in <see cref="SystemPruneResult"/>.
    /// </remarks>
    Task<CommandResponse<SystemPruneResult>> PruneAsync(
        DriverContext context,
        SystemPruneConfig? config = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Daemon Operations (Docker Desktop specific)

    /// <summary>
    /// Switches between Windows and Linux daemon modes (Docker Desktop only).
    /// </summary>
    Task<CommandResponse<Unit>> SwitchDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches to Linux daemon mode (Docker Desktop only).
    /// </summary>
    Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches to Windows daemon mode (Docker Desktop only).
    /// </summary>
    Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    #endregion
  }

  #region Info Types

  /// <summary>
  /// Common base for runtime/system information across drivers.
  /// Carries a meta dictionary with well-known keys for cross-driver consumers.
  /// </summary>
  public abstract class RuntimeInfoBase
  {
    /// <summary>
    /// Cross-driver metadata captured from runtime-specific inspect/version payloads.
    /// Keys are case-insensitive; prefer <see cref="SystemInfoMetaKeys"/> and
    /// <see cref="VersionInfoMetaKeys"/> where a well-known key exists.
    /// </summary>
    public Dictionary<string, string> MetaInfo { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a meta key/value if the value is non-null/non-empty.</summary>
    public void AddMeta(string key, object value) => SetMeta(key, value);

    /// <summary>
    /// Stores a metadata value using invariant formatting and ignores blank keys or values.
    /// </summary>
    /// <param name="key">Metadata key.</param>
    /// <param name="value">Metadata value.</param>
    protected void SetMeta(string key, object value)
    {
      if (string.IsNullOrWhiteSpace(key) || value == null)
        return;

      var valueString = value switch
      {
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
      };

      if (string.IsNullOrWhiteSpace(valueString))
        return;

      MetaInfo[key] = valueString;
    }
  }

  /// <summary>
  /// Well-known keys for <see cref="RuntimeInfoBase.MetaInfo"/> entries on <see cref="SystemInfo"/>.
  /// </summary>
  public static class SystemInfoMetaKeys
  {
    /// <summary>Runtime operating system name.</summary>
    public const string OperatingSystem = "operatingSystem";
    /// <summary>Runtime operating system type, usually <c>linux</c> or <c>windows</c>.</summary>
    public const string OSType = "osType";
    /// <summary>Runtime operating system version.</summary>
    public const string OSVersion = "osVersion";
    /// <summary>Runtime host architecture.</summary>
    public const string Architecture = "architecture";
    /// <summary>Runtime host name.</summary>
    public const string Hostname = "hostname";
    /// <summary>Container engine version.</summary>
    public const string EngineVersion = "engineVersion";
    /// <summary>Container storage backend.</summary>
    public const string StorageBackend = "storageBackend";
    /// <summary>Container logging backend.</summary>
    public const string LoggingBackend = "loggingBackend";
    /// <summary>Host kernel version.</summary>
    public const string KernelVersion = "kernelVersion";
    /// <summary>Total host memory in bytes.</summary>
    public const string MemoryTotal = "memoryTotal";
    /// <summary>Available CPU count.</summary>
    public const string Cpus = "cpus";
    /// <summary>Default OCI runtime name.</summary>
    public const string DefaultRuntime = "defaultRuntime";
    /// <summary>Known OCI runtime names.</summary>
    public const string Runtimes = "runtimes";
    /// <summary>Engine data-root path.</summary>
    public const string DataRoot = "dataRoot";
    /// <summary>Whether the runtime is operating rootless.</summary>
    public const string Rootless = "rootless";
  }

  /// <summary>
  /// Well-known keys for <see cref="RuntimeInfoBase.MetaInfo"/> entries on <see cref="VersionInfo"/>.
  /// </summary>
  public static class VersionInfoMetaKeys
  {
    /// <summary>Client version.</summary>
    public const string ClientVersion = "clientVersion";
    /// <summary>Client API version.</summary>
    public const string ClientApiVersion = "clientApiVersion";
    /// <summary>Server engine version.</summary>
    public const string ServerVersion = "serverVersion";
    /// <summary>Server API version.</summary>
    public const string ServerApiVersion = "serverApiVersion";
    /// <summary>Minimum supported API version.</summary>
    public const string MinApiVersion = "minApiVersion";
    /// <summary>Engine git commit.</summary>
    public const string GitCommit = "gitCommit";
    /// <summary>Runtime component version.</summary>
    public const string RuntimeVersion = "runtimeVersion";
    /// <summary>Operating system reported by the version endpoint.</summary>
    public const string Os = "os";
    /// <summary>Architecture reported by the version endpoint.</summary>
    public const string Arch = "arch";
    /// <summary>Engine build timestamp.</summary>
    public const string BuildTime = "buildTime";
    /// <summary>Whether experimental features are enabled.</summary>
    public const string Experimental = "experimental";
    /// <summary>Platform name reported by the runtime.</summary>
    public const string PlatformName = "platformName";
  }

  /// <summary>
  /// Cross-driver system information, mapped from <c>docker info</c> / <c>podman info</c>.
  /// This <c>FluentDocker.Drivers</c> type is the canonical port entity for system information
  /// crossing the driver boundary; it is distinct from adapter-specific parsing models.
  /// </summary>
  public class SystemInfo : RuntimeInfoBase
  {
    /// <summary>Operating system name (e.g., "Docker Desktop").</summary>
    public string OperatingSystem { get; set; }

    /// <summary>Operating system type (e.g., "linux", "windows").</summary>
    public string OSType { get; set; }

    /// <summary>Operating system version.</summary>
    public string OSVersion { get; set; }

    /// <summary>CPU architecture.</summary>
    public string Architecture { get; set; }

    /// <summary>Total number of containers.</summary>
    public int Containers { get; set; }

    /// <summary>Number of running containers.</summary>
    public int ContainersRunning { get; set; }

    /// <summary>Number of paused containers.</summary>
    public int ContainersPaused { get; set; }

    /// <summary>Number of stopped containers.</summary>
    public int ContainersStopped { get; set; }

    /// <summary>Total number of images.</summary>
    public int Images { get; set; }

    /// <summary>Engine/runtime version (e.g., Docker Engine version, Podman version).</summary>
    public string EngineVersion { get; set; }

    /// <summary>
    /// Backward-compatible alias for Docker-based consumers; maps to EngineVersion.
    /// </summary>
    public string ServerVersion
    {
      get => EngineVersion;
      set => EngineVersion = value;
    }

    /// <summary>Storage backend/driver in use.</summary>
    public string StorageBackend { get; set; }

    /// <summary>Logging backend/driver in use.</summary>
    public string LoggingBackend { get; set; }

    /// <summary>Kernel version.</summary>
    public string KernelVersion { get; set; }

    /// <summary>Total memory in bytes.</summary>
    public long MemoryTotal { get; set; }

    /// <summary>Number of CPUs.</summary>
    public int CPUs { get; set; }

    /// <summary>Root directory where the engine stores data/layers.</summary>
    public string DataRoot { get; set; }

    /// <summary>Server hostname.</summary>
    public string Hostname { get; set; }

    /// <summary>Security options.</summary>
    public List<string> SecurityOptions { get; set; } = [];

    /// <summary>Available runtimes reported by the engine.</summary>
    [JsonIgnore]
    public Dictionary<string, object> Runtimes { get; set; } = [];

    /// <summary>Default runtime.</summary>
    public string DefaultRuntime { get; set; }

    /// <summary>Whether the runtime reports rootless mode.</summary>
    public bool? Rootless { get; set; }

    /// <summary>
    /// Populate MetaInfo with a normalized set of cross-driver keys.
    /// </summary>
    public virtual void PopulateMeta()
    {
      SetMeta(SystemInfoMetaKeys.OperatingSystem, OperatingSystem);
      SetMeta(SystemInfoMetaKeys.OSType, OSType);
      SetMeta(SystemInfoMetaKeys.OSVersion, OSVersion);
      SetMeta(SystemInfoMetaKeys.Architecture, Architecture);
      SetMeta(SystemInfoMetaKeys.Hostname, Hostname);
      SetMeta(SystemInfoMetaKeys.EngineVersion, EngineVersion);
      SetMeta(SystemInfoMetaKeys.StorageBackend, StorageBackend);
      SetMeta(SystemInfoMetaKeys.LoggingBackend, LoggingBackend);
      SetMeta(SystemInfoMetaKeys.KernelVersion, KernelVersion);
      SetMeta(SystemInfoMetaKeys.MemoryTotal, MemoryTotal);
      SetMeta(SystemInfoMetaKeys.Cpus, CPUs);
      SetMeta(SystemInfoMetaKeys.DefaultRuntime, DefaultRuntime);
      SetMeta(SystemInfoMetaKeys.DataRoot, DataRoot);
      SetMeta(SystemInfoMetaKeys.Rootless, Rootless);

      if (Runtimes?.Count > 0)
        SetMeta(SystemInfoMetaKeys.Runtimes, string.Join(",", Runtimes.Keys));
    }
  }

  /// <summary>
  /// Cross-driver version information, mapped from <c>docker version</c> / <c>podman version</c>.
  /// </summary>
  public class VersionInfo : RuntimeInfoBase
  {
    /// <summary>Client version.</summary>
    public string ClientVersion { get; set; }

    /// <summary>Client API version.</summary>
    public string ClientApiVersion { get; set; }

    /// <summary>Server version.</summary>
    public string ServerVersion { get; set; }

    /// <summary>Server API version.</summary>
    public string ServerApiVersion { get; set; }

    /// <summary>Minimum API version.</summary>
    public string MinApiVersion { get; set; }

    /// <summary>Git commit.</summary>
    public string GitCommit { get; set; }

    /// <summary>Engine runtime language/toolchain version (e.g., Go version for Docker/Podman).</summary>
    public string RuntimeVersion { get; set; }

    /// <summary>Operating system.</summary>
    public string Os { get; set; }

    /// <summary>Architecture.</summary>
    public string Arch { get; set; }

    /// <summary>Build time.</summary>
    public string BuildTime { get; set; }

    /// <summary>Platform name (engine or desktop distribution).</summary>
    public string PlatformName { get; set; }

    /// <summary>Experimental features enabled.</summary>
    public bool Experimental { get; set; }

    /// <summary>
    /// Populate MetaInfo with cross-driver keys from resolved properties.
    /// </summary>
    public virtual void PopulateMeta()
    {
      SetMeta(VersionInfoMetaKeys.ClientVersion, ClientVersion);
      SetMeta(VersionInfoMetaKeys.ClientApiVersion, ClientApiVersion);
      SetMeta(VersionInfoMetaKeys.ServerVersion, ServerVersion);
      SetMeta(VersionInfoMetaKeys.ServerApiVersion, ServerApiVersion);
      SetMeta(VersionInfoMetaKeys.MinApiVersion, MinApiVersion);
      SetMeta(VersionInfoMetaKeys.GitCommit, GitCommit);
      SetMeta(VersionInfoMetaKeys.RuntimeVersion, RuntimeVersion);
      SetMeta(VersionInfoMetaKeys.Os, Os);
      SetMeta(VersionInfoMetaKeys.Arch, Arch);
      SetMeta(VersionInfoMetaKeys.BuildTime, BuildTime);
      SetMeta(VersionInfoMetaKeys.Experimental, Experimental);
      SetMeta(VersionInfoMetaKeys.PlatformName, PlatformName);
    }
  }

  /// <summary>Disk usage breakdown by resource kind, returned by <see cref="ISystemDriver.GetDiskUsageAsync"/>.</summary>
  public class DiskUsageInfo
  {
    /// <summary>Disk space used by images.</summary>
    public DiskUsageItem Images { get; set; } = new DiskUsageItem();

    /// <summary>Disk space used by containers.</summary>
    public DiskUsageItem Containers { get; set; } = new DiskUsageItem();

    /// <summary>Disk space used by volumes.</summary>
    public DiskUsageItem Volumes { get; set; } = new DiskUsageItem();

    /// <summary>Disk space used by build cache.</summary>
    public DiskUsageItem BuildCache { get; set; } = new DiskUsageItem();

    /// <summary>Total disk space used.</summary>
    public long TotalSize { get; set; }

    /// <summary>Total reclaimable space.</summary>
    public long Reclaimable { get; set; }
  }

  /// <summary>Disk usage counters for a single resource kind (images, containers, volumes, or build cache).</summary>
  public class DiskUsageItem
  {
    /// <summary>Total count.</summary>
    public int TotalCount { get; set; }

    /// <summary>Active count (in use).</summary>
    public int Active { get; set; }

    /// <summary>Total size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Reclaimable space in bytes.</summary>
    public long Reclaimable { get; set; }
  }

  #endregion

  #region Config Types

  /// <summary>Options for <see cref="ISystemDriver.PruneAsync"/>.</summary>
  public class SystemPruneConfig
  {
    /// <summary>Remove all unused images, not just dangling.</summary>
    public bool All { get; set; }

    /// <summary>Remove volumes.</summary>
    public bool Volumes { get; set; }

    /// <summary>Filter to provide.</summary>
    public Dictionary<string, string> Filter { get; set; } = [];
  }

  #endregion

  #region Result Types

  /// <summary>Resources removed by a <see cref="ISystemDriver.PruneAsync"/> call.</summary>
  public class SystemPruneResult
  {
    /// <summary>Deleted container IDs.</summary>
    public List<string> ContainersDeleted { get; set; } = [];

    /// <summary>Deleted image IDs.</summary>
    public List<string> ImagesDeleted { get; set; } = [];

    /// <summary>Deleted network IDs.</summary>
    public List<string> NetworksDeleted { get; set; } = [];

    /// <summary>Deleted volume names.</summary>
    public List<string> VolumesDeleted { get; set; } = [];

    /// <summary>Build cache entries deleted.</summary>
    public List<string> BuildCacheDeleted { get; set; } = [];

    /// <summary>Space reclaimed in bytes.</summary>
    public long SpaceReclaimed { get; set; }
  }

  #endregion
}
