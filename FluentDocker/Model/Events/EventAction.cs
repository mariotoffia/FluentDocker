namespace FluentDocker.Model.Events
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use stream driver ContainerEvent instead.")]
  public enum EventAction
  {
    Unspecified,
    Pull,
    Create,
    Start,
    Kill,
    Die,
    Connect,
    Disconnect,
    Stop,
    Destroy
  }
}
