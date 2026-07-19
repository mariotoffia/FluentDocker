#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// Marker for a compose service <c>volumes</c> entry, implemented by the short
  /// (<see cref="ShortServiceVolumeDefinition"/>) and long (<see cref="LongServiceVolumeDefinition"/>) syntax
  /// forms so both can be held in a single <see cref="ComposeServiceDefinition.Volumes"/> list.
  /// </summary>
  public interface IServiceVolumeDefinition
  {
  }
}
