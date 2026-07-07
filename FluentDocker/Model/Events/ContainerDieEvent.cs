#nullable enable
namespace FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a container has been buried (exited).
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public sealed class ContainerDieEvent : FdEvent<ContainerDieEvent.ContainerDieActor>
  {
    public ContainerDieEvent()
    {
      Action = EventAction.Die;
      Type = EventType.Container;
    }

    /// <summary>
    /// Contains the container hash, and which image is was created from.
    /// </summary>
    /// <remarks>
    /// The actor is the hash of the container.
    /// </remarks>
    [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
    public sealed class ContainerDieActor : EventActor
    {
      /// <summary>
      /// The image name and label such as "alpine:latest".
      /// </summary>
      public string? Image { get; set; }
      /// <summary>
      /// Name of the container.
      /// </summary>
      public string? Name { get; set; }
      /// <summary>
      /// The exit code that the container returned when died.
      /// </summary>
      public string? ExitCode { get; set; }
    }
  }
}
