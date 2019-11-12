namespace Ductus.FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a remote image has been pulled onto local store.
  /// </summary>
  public sealed class ImagePullEvent : FdEvent<ImagePullEvent.ImagePullActor>
  {
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
    public sealed class ImagePullActor : EventActor
    {
      /// <summary>
      /// Name of the image without label.
      /// </summary>
      public string Name { get; set; }
    }
  }
}
