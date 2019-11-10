namespace Ductus.FluentDocker.Model.HostEvents
{
  /// <summary>
  /// Base evnet emitte by the docker dameon using e.g. docker events.
  /// </summary>
  /// <remarks>
  /// See docker documentation https://docs.docker.com/engine/reference/commandline/events/
  /// </remarks>
  public class Event
  {
    /// <summary>
    /// The type of the event.
    /// </summary>
    public EventType Type { get; set; }
    /// <summary>
    /// The event action
    /// </summary>
    public EventAction Action { get; set; }
  }
}
