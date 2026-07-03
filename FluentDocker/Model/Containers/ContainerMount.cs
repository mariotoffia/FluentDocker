// ReSharper disable InconsistentNaming

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Mount entry from container inspect output.
  /// </summary>
  public sealed class ContainerMount
  {
    /// <summary>Mount or volume name.</summary>
    public string Name { get; set; }

    /// <summary>Host-side source path or volume source.</summary>
    public string Source { get; set; }

    /// <summary>Container-side destination path.</summary>
    public string Destination { get; set; }

    /// <summary>Volume driver name.</summary>
    public string Driver { get; set; }

    /// <summary>Mount mode string.</summary>
    public string Mode { get; set; }

    /// <summary>Whether the mount is read/write.</summary>
    public bool RW { get; set; }

    /// <summary>Mount propagation mode.</summary>
    public string Propagation { get; set; }
  }
}
