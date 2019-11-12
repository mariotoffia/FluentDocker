using System;
using System.Collections.Generic;

namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// The actor of a <see cref="FdEvent{T}"/> such as container id, image name or c# class name.
  /// </summary>
  public class EventActor
  {
    public string Id { get; internal set; }
    public IList<Tuple<string, string>> Labels { get; internal set; }
  }
}
