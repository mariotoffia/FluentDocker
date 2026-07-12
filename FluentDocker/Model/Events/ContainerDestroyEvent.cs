#nullable enable
namespace FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been removed from local disk (-rm operation).
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public sealed class ContainerDestroyEvent : FdEvent<ContainerDestroyEvent.ContainerDestroyActor>
  {
    /// <summary>
    /// Creates the event with <see cref="FdEvent.Action"/> set to <see cref="EventAction.Destroy"/> and
    /// <see cref="FdEvent.Type"/> set to <see cref="EventType.Container"/>.
    /// </summary>
    public ContainerDestroyEvent()
    {
      Action = EventAction.Destroy;
      Type = EventType.Container;
    }

    /// <summary>
    /// Contains the container hash, and which image is was created from.
    /// </summary>
    /// <remarks>
    /// The actor is the hash of the container.
    /// </remarks>
    [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
    public sealed class ContainerDestroyActor : EventActor
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
