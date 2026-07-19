#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>The mount type for a <see cref="LongServiceVolumeDefinition"/> (compose long-syntax <c>type</c> key).</summary>
  public enum VolumeType
  {
    /// <summary>A named or anonymous Docker volume (compose <c>type: volume</c>).</summary>
    Volume,
    /// <summary>A bind mount of a host path (compose <c>type: bind</c>).</summary>
    Bind,
    /// <summary>An in-memory tmpfs mount (compose <c>type: tmpfs</c>).</summary>
    TmpFs
  }
}
