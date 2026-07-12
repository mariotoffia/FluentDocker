#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// Marker for a compose service <c>ports</c> entry, implemented by the short
  /// (<see cref="PortsShortDefinition"/>) and long (<see cref="PortsLongDefinition"/>) syntax forms so both
  /// can be held in a single <see cref="ComposeServiceDefinition.Ports"/> list.
  /// </summary>
  public interface IPortsDefinition
  {
  }
}
