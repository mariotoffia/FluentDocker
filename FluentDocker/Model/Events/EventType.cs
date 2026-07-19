#nullable enable
namespace FluentDocker.Model.Events
{
  /// <summary>
  /// The object kind a Docker event pertains to, e.g. the <c>Type</c> field of <c>docker events</c> output.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public enum EventType
  {
    /// <summary>The raw type string did not map to any of the known event types below.</summary>
    Generic,

    /// <summary>The event pertains to an image.</summary>
    Image,

    /// <summary>The event pertains to a container.</summary>
    Container,

    /// <summary>The event pertains to a network.</summary>
    Network,

    /// <summary>The event pertains to a plugin.</summary>
    Plugin,

    /// <summary>The event pertains to a volume.</summary>
    Volume,

    /// <summary>The event pertains to the Docker daemon itself.</summary>
    Daemon,

    /// <summary>The event pertains to a Swarm service.</summary>
    Service,

    /// <summary>The event pertains to a Swarm node.</summary>
    Node,

    /// <summary>The event pertains to a Swarm secret.</summary>
    Secret,

    /// <summary>The event pertains to a Swarm config object.</summary>
    Config
  }
}
