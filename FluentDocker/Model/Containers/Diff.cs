#nullable enable
namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// A single filesystem-change row from a legacy <c>docker diff</c> parse.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use IContainerDriver DiffAsync filesystem changes instead.")]
  public sealed class Diff
  {
    /// <summary>The kind of change reported for <see cref="Item"/>.</summary>
    public DiffType Type { get; set; }

    /// <summary>The changed filesystem path.</summary>
    public string? Item { get; set; }
  }
}
