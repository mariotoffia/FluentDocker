#nullable enable
namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Specifies the container runtime type.
  /// </summary>
  public enum RuntimeType
  {
    // Member order is not a stable contract: serialize by name, never persist or transmit the numeric value.
    /// <summary>
    /// Unknown or custom runtime
    /// </summary>
    Unknown,

    /// <summary>
    /// Docker runtime
    /// </summary>
    Docker,

    /// <summary>
    /// Podman runtime
    /// </summary>
    Podman,

    /// <summary>
    /// Containerd runtime
    /// </summary>
    Containerd,

    /// <summary>
    /// CRI-O runtime
    /// </summary>
    CriO
  }
}
