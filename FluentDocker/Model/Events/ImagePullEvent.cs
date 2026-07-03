namespace FluentDocker.Model.Events
{
  /// <summary>
  /// Emitted when a remote image has been pulled onto local store.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use stream driver ContainerEvent instead.")]
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
    [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use stream driver ContainerEvent instead.")]
    public sealed class ImagePullActor : EventActor
    {
      /// <summary>
      /// Name of the image without label.
      /// </summary>
      public string Name { get; set; }
    }
  }
}
