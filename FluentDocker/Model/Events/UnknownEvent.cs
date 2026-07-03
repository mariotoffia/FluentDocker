using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Events
{
  /// <summary>
  /// All events that currently FluentDocker do not handle. Never rely or use
  /// this event for any logic if you're not prepare at any time replace that
  /// with a managed one!!!
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use stream driver ContainerEvent instead.")]
  public sealed class UnknownEvent : FdEvent<UnknownEvent.UnknownActor>
  {
    public UnknownEvent(string action, string type)
    {
      if (!Enum.TryParse<EventAction>(action, true, out var enumAction))
        enumAction = EventAction.Unspecified;

      if (!Enum.TryParse<EventType>(type, true, out var enumType))
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
    [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use stream driver ContainerEvent.ActorAttributes instead.")]
    public sealed class UnknownActor : EventActor
    {
      /// <summary>
      /// Attributes gathered from the raw data.
      /// </summary>
      public IList<Tuple<string, string>> Attributes { get; internal set; }
    }
  }
}
