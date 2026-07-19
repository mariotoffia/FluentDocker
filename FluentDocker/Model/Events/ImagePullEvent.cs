#nullable enable
namespace FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a remote image has been pulled onto local store.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public sealed class ImagePullEvent : FdEvent<ImagePullEvent.ImagePullActor>
  {
    /// <summary>
    /// Creates the event with <see cref="FdEvent.Action"/> set to <see cref="EventAction.Pull"/> and
    /// <see cref="FdEvent.Type"/> set to <see cref="EventType.Image"/>.
    /// </summary>
    public ImagePullEvent()
    {
      Action = EventAction.Pull;
      Type = EventType.Image;
    }

    /// <summary>
    /// The actor is the image name and label
    /// </summary>
    /// <remarks>
    /// The <see cref="EventActor.Id"/> contain the "image name:label".
    /// </remarks>
    [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
    public sealed class ImagePullActor : EventActor
    {
      /// <summary>
      /// Name of the image without label.
      /// </summary>
      public string? Name { get; set; }
    }
  }
}
