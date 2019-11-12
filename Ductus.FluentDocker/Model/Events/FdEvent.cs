using System;

namespace Ductus.FluentDocker.Model.Events
{
  public abstract class FdEvent
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
    public EventActor EventActor { get; set; }

    /// <summary>
    /// Timestamp in nanoseconds.
    /// </summary>
    public DateTime Time { get; set; }

  }

  /// <summary>
  /// Base event in the system.
  /// </summary>
  /// <typeparam name="T"></typeparam>
  public abstract class FdEvent<T> : FdEvent where T : EventActor
  {
    /// <summary>
    /// The actor that is the originator of this event.
    /// </summary>
    public new T EventActor { get; set; }
  }
}
