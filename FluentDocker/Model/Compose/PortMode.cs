#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>How a published port (<see cref="PortsLongDefinition.Mode"/>) is exposed by the swarm.</summary>
  public enum PortMode
  {
    /// <summary>Publishes a host port on each node (compose <c>mode: host</c>).</summary>
    Host,
    /// <summary>Publishes a swarm-mode port that is load balanced across nodes (compose <c>mode: ingress</c>).</summary>
    Ingress
  }
}
