namespace FluentDocker.Services
{
  /// <summary>
  /// Allows callers to query which lifecycle operations a service supports,
  /// so UI/orchestration code can hide operations that are not meaningful for a resource type.
  /// </summary>
  /// <remarks>
  /// A <c>false</c> capability means the operation has no daemon-side lifecycle meaning for that
  /// service. Built-in static resources may implement such methods as documented no-ops
  /// (for example image/network/volume start), while nonsensical operations throw
  /// <see cref="FluentDockerNotSupportedException"/>.
  /// </remarks>
  public interface IServiceCapabilities
  {
    /// <summary>True when start is a meaningful lifecycle operation for this service.</summary>
    bool CanStart { get; }

    /// <summary>True when stop is a meaningful lifecycle operation for this service.</summary>
    bool CanStop { get; }

    /// <summary>True when the service can be paused; false means <c>PauseAsync</c> throws.</summary>
    bool CanPause { get; }

    /// <summary>True when the service can be removed.</summary>
    bool CanRemove { get; }

    /// <summary>True when the service accepts lifecycle state hooks.</summary>
    bool CanHook { get; }
  }
}
