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
    public string Id { get; internal set; } = null!;
    public IList<Tuple<string, string>>? Labels { get; internal set; }
  }
}
