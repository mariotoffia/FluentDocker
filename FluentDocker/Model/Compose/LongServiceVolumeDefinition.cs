using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   Mount host paths or named volumes, specified as sub-options to a service.
  /// </summary>
  /// <remarks>
  ///   You can mount a host path as part of a definition for a single service, and there is no need to define it in the top
  ///   level volumes key. But, if you want to reuse a volume across multiple services, then define a named volume in the
  ///   top-level volumes key. Use named volumes with services, swarms, and stack files. Note: The top-level volumes key
  ///   defines a named volume and references it from each service’s volumes list. This replaces volumes_from in earlier
  ///   versions of the Compose file format. See Use volumes and Volume Plugins for general information on volumes. Note: The
  ///   long syntax is new in v3.2
  /// </remarks>
  public sealed class LongServiceVolumeDefinition : IServiceVolumeDefinition
  {
    /// <summary>
    ///   the source of the mount, a path on the host for a bind mount, or the name of a volume defined in the top-level
    ///   volumes key. Not applicable for a tmpfs mount.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    ///   the path in the container where the volume is mounted.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    ///   The mount type.
    /// </summary>
    public VolumeType Type { get; set; }

    /// <summary>
    ///   flag to set the volume as read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    ///   Options if any.
    /// </summary>
    /// <remarks>
    ///   * bind-> propagation: the propagation mode used for the bind.
    ///   * volume -> nocopy: flag to disable copying of data from a container when a volume is created.
    ///   * tmpfs -> size: the size for the tmpfs mount in bytes.
    /// </remarks>
    public IDictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
  }
}
