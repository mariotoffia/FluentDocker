#nullable enable
namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Windows container isolation mode for <c>docker build</c>/<c>run --isolation</c>. No-op on Linux daemons.
  /// </summary>
  public enum ContainerIsolationTechnology
  {
    /// <summary>No isolation value was set; not rendered as a CLI flag.</summary>
    Unknown = 0,

    /// <summary>Use the daemon's configured default isolation technology.</summary>
    Default = 1,

    /// <summary>Process (Windows Server container) isolation — shares the host kernel.</summary>
    Process = 2,

    /// <summary>Hyper-V isolation — runs the container in a lightweight utility VM.</summary>
    Hyperv = 3
  }
}
