namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been sent a kill signal (but is not yet dead).
  /// </summary>
  public sealed class ContainerKillEvent : FdEvent<ContainerKillEvent.ContainerKillActor>
  {
    public ContainerKillEvent()
    {
      Action = EventAction.Kill;
      Type = EventType.Container;
    }

    /// <summary>
    /// Contains the container hash, and which image is was created from.
    /// </summary>
    /// <remarks>
    /// The actor is the hash of the container.
    /// </remarks>
    public sealed class ContainerKillActor : EventActor
    {
      /// <summary>
      /// The image name and label such as "alpine:latest".
      /// </summary>
      public string Image { get; set; }
      /// <summary>
      /// Name of the container.
      /// </summary>
      public string Name { get; set; }
      /// <summary>
      /// The signal that the container has been signalled.
      /// </summary>
      public string Signal { get; set; }
    }
  }
}
