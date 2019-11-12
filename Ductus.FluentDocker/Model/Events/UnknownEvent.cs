using System;
using System.Collections.Generic;
using System.Text;

namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// All events that currently FluentDocker do not handle. Never rely or use
  /// this event for any logic if you're not prepare at any time replace that
  /// with a managed one!!!
  /// </summary>
  public sealed class UnknownEvent : FdEvent<UnknownEvent.UnknownActor>
  {
    public UnknownEvent(string action, string type)
    {
      if (!Enum.TryParse<EventAction>(action, out var enumAction))
        enumAction = EventAction.Unspecified;

      if (!Enum.TryParse<EventType>(type, out var enumType))
        enumType = EventType.Generic;

      Action = enumAction;
      Type = enumType;
      ActionRaw = action;
      TypeRaw = type;
    }

    /// <summary>
    /// The raw string gotten from the event stream.
    /// </summary>
    public string ActionRaw { get; }
    /// <summary>
    /// The raw string gotten from the event stream.
    /// </summary>
    public string TypeRaw { get; }
    /// <summary>
    /// Contains Id and all attributes it could gather.
    /// </summary>
    public sealed class UnknownActor : EventActor
    {
      /// <summary>
      /// Attributes gathered from the raw data.
      /// </summary>
      public IList<Tuple<string, string>> Attributes { get; internal set; }
    }
  }
}
