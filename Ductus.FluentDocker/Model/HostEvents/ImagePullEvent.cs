namespace Ductus.FluentDocker.Model.HostEvents
{
  public sealed class ImagePullEvent : Event<ImagePullEvent.ImagePullActor>
  {
    public ImagePullEvent()
    {
      Action = EventAction.Pull;
      Type = EventType.Image;
    }

    public sealed class ImagePullActor : EventActor
    {
      public string ImageName { get; set; }
    }
  }
}
