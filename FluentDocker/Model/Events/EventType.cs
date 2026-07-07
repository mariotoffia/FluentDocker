#nullable enable
namespace FluentDocker.Model.Events
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public enum EventType
  {
    Generic,
    Image,
    Container,
    Network,
    Plugin,
    Volume,
    Daemon,
    Service,
    Node,
    Secret,
    Config
  }
}
