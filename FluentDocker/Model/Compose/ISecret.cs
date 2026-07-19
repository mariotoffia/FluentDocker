#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>
  /// Marker for a compose service <c>secrets</c> entry, implemented by the short
  /// (<see cref="ShortSecret"/>) and long (<see cref="LongSecret"/>) syntax forms so both can be held in a
  /// single <see cref="ComposeServiceDefinition.Secrets"/> list.
  /// </summary>
  public interface ISecret
  {

  }
}
