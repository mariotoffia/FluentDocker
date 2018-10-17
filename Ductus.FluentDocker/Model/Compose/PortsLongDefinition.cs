namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   The long form syntax allows the configuration of additional fields that can’t be expressed in the
  ///   <see cref="PortsShortDefinition" /> form.
  /// </summary>
  /// <remarks>
  /// Note: The long syntax is new in v3.2
  /// </remarks>
  /// <example>
  /// ports:
  /// - target: 80
  /// published: 8080
  /// protocol: tcp
  /// mode: host
  /// </example>
  public sealed class PortsLongDefinition : IPortsDefinition
  {
    /// <summary>
    /// Target, inside container, port e.g 80
    /// </summary>
    public int Target { get; set; }
    /// <summary>
    /// Published, out from the container, port e.g. 8080.
    /// </summary>
    public int Published { get; set; }
    /// <summary>
    /// The protocol e.g. udp or tcp. Default is tcp.
    /// </summary>
    public string Protocol { get; set; } = "tcp";
    /// <summary>
    /// The mode of publishing (host or ingress). Default is host.
    /// </summary>
    /// <remarks>
    /// Use host for publishing a host port on each node, or ingress for a swarm mode port to be load balanced.
    /// </remarks>
    public PortMode Mode { get; set; } = PortMode.Host;
  }
}