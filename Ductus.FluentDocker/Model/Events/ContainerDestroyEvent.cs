namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been removed from local disk (-rm operation).
  /// </summary>
  public sealed class ContainerDestroyEvent : FdEvent<ContainerDestroyEvent.ContainerDestroyActor>
  {
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
    public sealed class ContainerDestroyActor : EventActor
    {
      /// <summary>
      /// The image name and label such as "alpine:latest".
      /// </summary>
      public string Image { get; set; }
      /// <summary>
      /// Name of the container.
      /// </summary>
      public string Name { get; set; }
    }
  }
}
