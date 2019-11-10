using System;

namespace Ductus.FluentDocker.Model.HostEvents
{
  /// <summary>
  /// Base evnet emitte by the docker dameon using e.g. docker events.
  /// </summary>
  /// <remarks>
  /// See docker documentation https://docs.docker.com/engine/reference/commandline/events/
  /// </remarks>
  public class Event<T> where T : EventActor
  {
    /// <summary>
    /// The type of the event.
    /// </summary>
    public EventType Type { get; set; }
    /// <summary>
    /// The event action
    /// </summary>
    public EventAction Action { get; set; }

    public EventScope Scope { get; set; }

    /// <summary>
    /// Timestamp in nanoseconds.
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// The actor that is the origin of this event.
    /// </summary>
    public T Actor { get; set; }
  }
}
