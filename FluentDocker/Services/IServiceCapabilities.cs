namespace FluentDocker.Services
{
  /// <summary>
  /// Allows callers to query which lifecycle operations a service supports,
  /// preventing runtime <see cref="FluentDockerNotSupportedException"/> from unsupported operations.
  /// </summary>
  public interface IServiceCapabilities
  {
    /// <summary>True when the service can be started.</summary>
    bool CanStart { get; }

    /// <summary>True when the service can be stopped.</summary>
    bool CanStop { get; }

    /// <summary>True when the service can be paused; false means <c>PauseAsync</c> throws.</summary>
    bool CanPause { get; }

    /// <summary>True when the service can be removed.</summary>
    bool CanRemove { get; }

    /// <summary>True when the service accepts lifecycle state hooks.</summary>
    bool CanHook { get; }
  }
}
