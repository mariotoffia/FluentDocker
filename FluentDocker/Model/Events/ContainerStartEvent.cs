#nullable enable
namespace FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been started.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public sealed class ContainerStartEvent : FdEvent<ContainerStartEvent.ContainerStartActor>
  {
    /// <summary>
    /// Creates the event with <see cref="FdEvent.Action"/> set to <see cref="EventAction.Start"/> and
    /// <see cref="FdEvent.Type"/> set to <see cref="EventType.Container"/>.
    /// </summary>
    public ContainerStartEvent()
    {
      Action = EventAction.Start;
      Type = EventType.Container;
    }

    /// <summary>
    /// Contains the container hash, and which image is was created from.
    /// </summary>
    /// <remarks>
    /// The actor is the hash of the container.
    /// </remarks>
    [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
    public sealed class ContainerStartActor : EventActor
    {
      /// <summary>
      /// The image name and label such as "alpine:latest".
      /// </summary>
      public string? Image { get; set; }
      /// <summary>
      /// Name of the container.
      /// </summary>
      public string? Name { get; set; }
    }
  }
}
