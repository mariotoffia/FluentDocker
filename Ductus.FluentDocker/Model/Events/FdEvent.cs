using System;

namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Base event in the system.
  /// </summary>
  /// <typeparam name="T"></typeparam>
  public abstract class FdEvent<T> where T : EventActor
  {
    /// <summary>
    /// The type of the event.
    /// </summary>
    /// <remarks>
    ///   If not present the <see cref="EventType.Generic"/> is provided.
    /// </remarks>
    public EventType Type { get; set; }

    /// <summary>
    /// The scope of the event.
    /// </summary>
    public EventScope Scope { get; set; }

    /// <summary>
    /// The event action
    /// </summary>
    public EventAction Action { get; set; }

    /// <summary>
    /// The actor that is the originator of this event.
    /// </summary>
    public T EventActor { get; set; }

    /// <summary>
    /// Timestamp in nanoseconds.
    /// </summary>
    public DateTime Time { get; set; }
  }
}
