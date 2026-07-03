namespace FluentDocker.Model.Events
{
  /// <summary>
  /// The scope of the <see cref="FdEvent{T}"/>.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use stream driver ContainerEvent.Scope instead.")]
  public enum EventScope
  {
    /// <summary>
    /// Unknown
    /// </summary>
    Unknown,
    /// <summary>
    /// Local scope, i.e. the event originated from local docker host.
    /// </summary>
    Local,
    /// <summary>
    /// Swarm scope, i.e. the event originated from Docker Swarm.
    /// </summary>
    Swarm
  }
}
