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
  ///   versions of the Compose file format. See Use volumes and Volume Plugins for general information on volumes.
  /// </remarks>
  public sealed class ShortServiceVolumeDefinition : IServiceVolumeDefinition
  {
    /// <summary>
    ///   Single volume mapping entry.
    /// </summary>
    /// <remarks>
    ///   Optionally specify a path on the host machine (HOST:CONTAINER), or an access mode (HOST:CONTAINER:ro).
    ///   You can mount a relative path on the host, that expands relative to the directory of the Compose configuration
    ///   file being used. Relative paths should always begin with . or ...
    /// </remarks>
    /// <example>
    ///   volumes:
    ///   # Just specify a path and let the Engine create a volume
    ///   - /var/lib/mysql
    ///   # Specify an absolute path mapping
    ///   - /opt/data:/var/lib/mysql
    ///   # Path on the host, relative to the Compose file
    ///   - ./cache:/tmp/cache
    ///   # User-relative path
    ///   - ~/configs:/etc/configs/:ro
    ///   # Named volume
    ///   - datavolume:/var/lib/mysql
    /// </example>
    public string Entry { get; set; }
  }
}