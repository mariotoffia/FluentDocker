using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Volumes
{
  /// <summary>
  /// Represents a Docker/Podman volume as returned by volume inspect or list.
  /// </summary>
  public sealed class Volume
  {
    /// <summary>Timestamp when the volume was created.</summary>
    public DateTime Created { get; set; }

    /// <summary>Volume driver name (e.g., "local").</summary>
    public string Driver { get; set; }

    /// <summary>Unique name of the volume.</summary>
    public string Name { get; set; }

    /// <summary>Scope of the volume ("local" or "global").</summary>
    public string Scope { get; set; }

    /// <summary>
    /// Filesystem path where the volume data is stored on the host.
    /// </summary>
    public string Mountpoint { get; set; }

    /// <summary>
    /// User-defined labels attached to the volume.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; }

    /// <summary>
    /// Driver-specific options used when creating the volume.
    /// </summary>
    public Dictionary<string, string> Options { get; set; }
  }
}
