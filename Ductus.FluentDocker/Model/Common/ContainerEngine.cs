namespace Ductus.FluentDocker.Model.Common
{
  /// <summary>
  /// Specifies the container engine to use.
  /// </summary>
  /// <remarks>
  /// This differs from <see cref="Ductus.FluentDocker.Model.Containers.ContainerRuntime"/> which specifies
  /// the OCI runtime (like runc or nvidia) used by the container engine.
  /// </remarks>
  public enum ContainerEngine
  {
    /// <summary>
    /// Automatically detect the available container engine.
    /// Docker is preferred if both Docker and Podman are available.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Use Docker as the container engine.
    /// </summary>
    Docker = 1,

    /// <summary>
    /// Use Podman as the container engine.
    /// Podman is an open-source, daemonless container engine that is a drop-in replacement for Docker.
    /// </summary>
    Podman = 2
  }
}
