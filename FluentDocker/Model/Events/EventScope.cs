namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// The scope of the <see cref="FdEvent{T}"/>.
  /// </summary>
  public enum EventScope
  {
    /// <summary>
    /// Unknown
    /// </summary>
    Unknown,
    /// <summary>
    /// Local scope, i.e. the event originated from local docker host.
    /// </summary>
    Local
  }
}
