using System.Collections.Generic;
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
        /// Checks if the engine is Linux-based.
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
        Task<CommandResponse<SystemPruneResult>> PruneAsync(
            DriverContext context,
            SystemPruneConfig config = null,
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

    public class SystemInfo
    {
        /// <summary>Operating system type.</summary>
        public string OperatingSystem { get; set; }

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

        /// <summary>Server version.</summary>
        public string ServerVersion { get; set; }

        /// <summary>Storage driver in use.</summary>
        public string StorageDriver { get; set; }

        /// <summary>Logging driver in use.</summary>
        public string LoggingDriver { get; set; }

        /// <summary>Kernel version.</summary>
        public string KernelVersion { get; set; }

        /// <summary>Total memory in bytes.</summary>
        public long MemoryTotal { get; set; }

        /// <summary>Number of CPUs.</summary>
        public int CPUs { get; set; }

        /// <summary>Docker root directory.</summary>
        public string DockerRootDir { get; set; }

        /// <summary>Server hostname.</summary>
        public string Hostname { get; set; }

        /// <summary>Whether swarm mode is active.</summary>
        public bool SwarmActive { get; set; }

        /// <summary>Security options.</summary>
        public List<string> SecurityOptions { get; set; } = new List<string>();

        /// <summary>Available runtimes.</summary>
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>Default runtime.</summary>
        public string DefaultRuntime { get; set; }
    }

    public class VersionInfo
    {
        /// <summary>Client version.</summary>
        public string ClientVersion { get; set; }

        /// <summary>Client API version.</summary>
        public string ClientApiVersion { get; set; }

        /// <summary>Server version.</summary>
        public string ServerVersion { get; set; }

        /// <summary>Server API version.</summary>
        public string ServerApiVersion { get; set; }

        /// <summary>Git commit.</summary>
        public string GitCommit { get; set; }

        /// <summary>Go version.</summary>
        public string GoVersion { get; set; }

        /// <summary>Operating system.</summary>
        public string Os { get; set; }

        /// <summary>Architecture.</summary>
        public string Arch { get; set; }

        /// <summary>Minimum API version.</summary>
        public string MinApiVersion { get; set; }

        /// <summary>Build time.</summary>
        public string BuildTime { get; set; }

        /// <summary>Experimental features enabled.</summary>
        public bool Experimental { get; set; }
    }

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

    public class SystemPruneConfig
    {
        /// <summary>Remove all unused images, not just dangling.</summary>
        public bool All { get; set; }

        /// <summary>Remove volumes.</summary>
        public bool Volumes { get; set; }

        /// <summary>Filter to provide.</summary>
        public Dictionary<string, string> Filter { get; set; } = new Dictionary<string, string>();
    }

    #endregion

    #region Result Types

    public class SystemPruneResult
    {
        /// <summary>Deleted container IDs.</summary>
        public List<string> ContainersDeleted { get; set; } = new List<string>();

        /// <summary>Deleted image IDs.</summary>
        public List<string> ImagesDeleted { get; set; } = new List<string>();

        /// <summary>Deleted network IDs.</summary>
        public List<string> NetworksDeleted { get; set; } = new List<string>();

        /// <summary>Deleted volume names.</summary>
        public List<string> VolumesDeleted { get; set; } = new List<string>();

        /// <summary>Build cache entries deleted.</summary>
        public List<string> BuildCacheDeleted { get; set; } = new List<string>();

        /// <summary>Space reclaimed in bytes.</summary>
        public long SpaceReclaimed { get; set; }
    }

    #endregion
}
