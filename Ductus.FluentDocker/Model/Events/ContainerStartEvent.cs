namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been started.
  /// </summary>
  public sealed class ContainerStartEvent : FdEvent<ContainerStartEvent.ContainerStartActor>
  {
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
    public sealed class ContainerStartActor : EventActor
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
