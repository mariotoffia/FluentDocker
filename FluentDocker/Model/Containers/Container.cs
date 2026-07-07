#nullable enable
namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Container inspect DTO returned by Docker and Podman drivers.
  /// </summary>
  public sealed class Container
  {
    /// <summary>Container ID.</summary>
    public string? Id { get; set; }

    /// <summary>Image ID or image reference used by the container.</summary>
    public string? Image { get; set; }

    /// <summary>Container creation timestamp.</summary>
    public System.DateTimeOffset Created { get; set; }

    /// <summary>Path to the generated resolv.conf file.</summary>
    public string? ResolvConfPath { get; set; }

    /// <summary>Path to the generated hostname file.</summary>
    public string? HostnamePath { get; set; }

    /// <summary>Path to the generated hosts file.</summary>
    public string? HostsPath { get; set; }

    /// <summary>Container log file path.</summary>
    public string? LogPath { get; set; }

    /// <summary>Container name.</summary>
    public string? Name { get; set; }

    /// <summary>Number of times the runtime restarted the container.</summary>
    public int RestartCount { get; set; }

    /// <summary>Storage driver used by the container.</summary>
    public string? Driver { get; set; }

    /// <summary>Command arguments used to start the container.</summary>
    public string[]? Args { get; set; }

    /// <summary>Runtime state from inspect.</summary>
    public ContainerState? State { get; set; }

    /// <summary>Mounted filesystems and volumes.</summary>
    public ContainerMount[]? Mounts { get; set; }

    /// <summary>Container configuration from inspect.</summary>
    public ContainerConfig? Config { get; set; }

    /// <summary>Network settings from inspect.</summary>
    public ContainerNetworkSettings? NetworkSettings { get; set; }
  }
}
