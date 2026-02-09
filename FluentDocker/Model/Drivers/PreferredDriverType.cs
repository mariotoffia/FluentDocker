namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Specifies the preferred driver type when multiple drivers are available.
  /// </summary>
  public enum PreferredDriverType
  {
    /// <summary>
    /// Prefer CLI-based drivers (docker cli, podman cli)
    /// </summary>
    Cli,

    /// <summary>
    /// Prefer API-based drivers (docker api, podman api)
    /// </summary>
    Api,

    /// <summary>
    /// Prefer Docker drivers over Podman
    /// </summary>
    Docker,

    /// <summary>
    /// Prefer Podman drivers over Docker
    /// </summary>
    Podman,

    /// <summary>
    /// Use the first available driver
    /// </summary>
    Any
  }
}
