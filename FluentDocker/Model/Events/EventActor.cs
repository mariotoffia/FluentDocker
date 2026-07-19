#nullable enable
using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Events
{
  /// <summary>
  /// The actor of a <see cref="FdEvent{T}"/> such as container id, image name or c# class name.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public class EventActor
  {
    /// <summary>The id (hash) of the entity that originated the event, e.g. a container or image id.</summary>
    public string Id { get; internal set; } = null!;

    /// <summary>Key/value labels attached to the originating entity, as reported by the event stream.</summary>
    public IList<Tuple<string, string>>? Labels { get; internal set; }
  }
}
