namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   Either specify both ports (HOST:CONTAINER), or just the container port (an ephemeral host port is chosen).
  /// </summary>
  /// <remarks>
  ///   Note: When mapping ports in the HOST:CONTAINER format, you may experience erroneous results when using a container
  ///   port lower than 60, because YAML parses numbers in the format xx:yy as a base-60 value. For this reason, we recommend
  ///   always explicitly specifying your port mappings as strings.
  /// </remarks>
  /// <example>
  /// ports:
  /// - "3000"
  /// - "3000-3005"
  /// - "8000:8000"
  /// - "9090-9091:8080-8081"
  /// - "49100:22"
  /// - "127.0.0.1:8001:8001"
  /// - "127.0.0.1:5000-5010:5000-5010"
  /// - "6060:6060/udp"
  /// </example>
  public sealed class PortsShortDefinition : IPortsDefinition
  {
    /// <summary>
    /// The entry on the specified form of this class documentation.
    /// </summary>
    public string Entry { get; set; }
  }
}
