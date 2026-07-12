#nullable enable
namespace FluentDocker.Model.Compose
{
  /// <summary>Top-level compose <c>volumes</c> entry: a named volume that services can mount.</summary>
  public sealed class ComposeVolumeDefinition
  {
    /// <summary>The volume name (the key under the top-level <c>volumes</c> map).</summary>
    public string? Name { get; set; }
  }
}
