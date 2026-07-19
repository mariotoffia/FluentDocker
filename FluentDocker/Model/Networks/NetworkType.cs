#nullable enable
namespace FluentDocker.Model.Networks
{
  /// <summary>
  /// The Docker network driver a network was created with (the <c>-d</c>/<c>--driver</c> value of
  /// <c>docker network create</c>).
  /// </summary>
  public enum NetworkType
  {
    /// <summary>The driver name did not map to any of the known built-in drivers below.</summary>
    Unknown = 0,

    /// <summary>The default single-host <c>bridge</c> driver.</summary>
    Bridge,

    /// <summary>The <c>host</c> driver; containers share the host's network namespace.</summary>
    Host,

    /// <summary>The <c>overlay</c> driver, used for multi-host Swarm networks.</summary>
    Overlay,

    /// <summary>The <c>ipvlan</c> driver.</summary>
    Ipvlan,

    /// <summary>The <c>macvlan</c> driver.</summary>
    Macvlan,

    /// <summary>The <c>null</c> driver; networking is disabled.</summary>
    None,

    /// <summary>A third-party plugin driver; its name is carried alongside this value (e.g. <c>CustomType</c>).</summary>
    Custom
  }
}
