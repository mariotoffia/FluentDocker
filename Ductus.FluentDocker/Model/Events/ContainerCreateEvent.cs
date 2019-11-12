namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been created (not started).
  /// </summary>
  public sealed class ContainerCreateEvent : FdEvent<ContainerCreateEvent.ContainerCreateActor>
  {
    public ContainerCreateEvent()
    {
      Action = EventAction.Create;
      Type = EventType.Container;
    }

    /// <summary>
    /// Contains the container hash, and which image is was created from.
    /// </summary>
    /// <remarks>
    /// The actor is the hash of the container.
    /// </remarks>
    public sealed class ContainerCreateActor : EventActor
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
