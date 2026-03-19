namespace FluentDocker.Services
{
  /// <summary>
  /// Represents the lifecycle state of a Docker/Podman service.
  /// </summary>
  public enum ServiceRunningState
  {
    /// <summary>State is not known or has not been queried.</summary>
    Unknown = 0,

    /// <summary>Service is in the process of starting.</summary>
    Starting = 1,

    /// <summary>Service is running and healthy.</summary>
    Running = 2,

    /// <summary>Service is paused and can be resumed.</summary>
    Paused = 3,

    /// <summary>Service is in the process of stopping.</summary>
    Stopping = 4,

    /// <summary>Service has been stopped but not removed.</summary>
    Stopped = 5,

    /// <summary>Service is in the process of being removed.</summary>
    Removing = 6,

    /// <summary>Service has been removed and is no longer available.</summary>
    Removed = 7
  }
}
