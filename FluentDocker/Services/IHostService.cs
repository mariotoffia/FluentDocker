using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;

#pragma warning disable CS0618 // Services.NetworkCreateConfig obsolete — intentional usage in public API

namespace FluentDocker.Services
{
  /// <summary>
  /// Async host service interface for managing Docker hosts.
  /// </summary>
  public interface IHostService : IServiceAsync
  {
    /// <summary>
    /// Whether this is a native Docker installation.
    /// </summary>
    bool IsNative { get; }

    /// <summary>
    /// Whether TLS is required for connections.
    /// </summary>
    bool RequireTls { get; }

    #region System Information

    /// <summary>
    /// Gets system information asynchronously.
    /// </summary>
    Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets version information asynchronously.
    /// </summary>
    Task<VersionInfo> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pings the daemon to check if it's responsive.
    /// </summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets disk usage information asynchronously.
    /// </summary>
    Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Container Management

    /// <summary>
    /// Gets running containers.
    /// </summary>
    Task<IList<IContainerService>> GetRunningContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets containers with optional filters.
    /// </summary>
    Task<IList<IContainerService>> GetContainersAsync(
        bool all = true,
        IDictionary<string, string> filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new container (not started).
    /// </summary>
    Task<IContainerService> CreateContainerAsync(
        string image,
        ContainerCreateOptions config = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Image Management

    /// <summary>
    /// Gets images with optional filters.
    /// </summary>
    Task<IList<IImageService>> GetImagesAsync(
        bool all = true,
        ImageListFilter filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls an image from a registry.
    /// </summary>
    Task<IImageService> PullImageAsync(
        string image,
        string tag = "latest",
        IProgress<ImagePullProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds an image from a Dockerfile.
    /// </summary>
    Task<IImageService> BuildImageAsync(
        ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Network Management

    /// <summary>
    /// Gets all networks.
    /// </summary>
    Task<IList<INetworkService>> GetNetworksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a network.
    /// </summary>
    Task<INetworkService> CreateNetworkAsync(
        string name,
        NetworkCreateConfig config = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Volume Management

    /// <summary>
    /// Gets all volumes.
    /// </summary>
    Task<IList<IVolumeService>> GetVolumesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a volume.
    /// </summary>
    Task<IVolumeService> CreateVolumeAsync(
        string name = null,
        string driver = "local",
        IDictionary<string, string> labels = null,
        IDictionary<string, string> options = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Maintenance

    /// <summary>
    /// Prunes unused resources.
    /// </summary>
    Task<SystemPruneResult> PruneAsync(
        SystemPruneConfig config = null,
        CancellationToken cancellationToken = default);

    #endregion
  }

  /// <summary>
  /// Options for creating a container.
  /// </summary>
  public class ContainerCreateOptions
  {
    /// <summary>Force pull the image before creating.</summary>
    public bool ForcePull { get; set; }

    /// <summary>Container name.</summary>
    public string Name { get; set; }

    /// <summary>Command to run.</summary>
    public string[] Command { get; set; }

    /// <summary>Environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

    /// <summary>Port mappings (container:host).</summary>
    public Dictionary<string, string> Ports { get; set; } = new Dictionary<string, string>();

    /// <summary>Volume mounts.</summary>
    public List<string> Volumes { get; set; } = new List<string>();

    /// <summary>Network to connect to.</summary>
    public string Network { get; set; }

    /// <summary>Working directory inside container.</summary>
    public string WorkingDir { get; set; }

    /// <summary>User to run as.</summary>
    public string User { get; set; }

    /// <summary>Stop container on service dispose.</summary>
    public bool StopOnDispose { get; set; } = true;

    /// <summary>Delete container on service dispose.</summary>
    public bool DeleteOnDispose { get; set; } = true;

    /// <summary>Delete volumes on container dispose.</summary>
    public bool DeleteVolumeOnDispose { get; set; }

    /// <summary>Delete named volumes on container dispose.</summary>
    public bool DeleteNamedVolumeOnDispose { get; set; }

    /// <summary>Labels to apply.</summary>
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>Memory limit in bytes.</summary>
    public long? MemoryLimit { get; set; }

    /// <summary>CPU quota.</summary>
    public long? CpuQuota { get; set; }

    /// <summary>Restart policy.</summary>
    public string RestartPolicy { get; set; }

    /// <summary>Privileged mode.</summary>
    public bool Privileged { get; set; }
  }

  /// <summary>
  /// Configuration for creating a network.
  /// </summary>
  [Obsolete("Use FluentDocker.Drivers.NetworkCreateConfig instead. " +
            "This duplicate type in the Services namespace will be removed in v4.")]
  public class NetworkCreateConfig
  {
    /// <summary>Network driver (default: bridge).</summary>
    public string Driver { get; set; } = "bridge";

    /// <summary>Internal network (no external connectivity).</summary>
    public bool Internal { get; set; }

    /// <summary>Enable IPv6.</summary>
    public bool EnableIPv6 { get; set; }

    /// <summary>Network labels.</summary>
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>Driver options.</summary>
    public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

    /// <summary>Remove on service dispose.</summary>
    public bool RemoveOnDispose { get; set; }
  }
}

