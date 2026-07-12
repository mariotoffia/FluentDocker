#nullable enable
namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Kind of filesystem change reported by a legacy <c>docker diff</c> row (single-letter prefix: A/U/R/C).
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use FilesystemChange.Kind instead.")]
  public enum DiffType
  {
    /// <summary>Path was added ('A' row prefix).</summary>
    Added = 1,

    /// <summary>Path was removed ('R' row prefix).</summary>
    Removed = 2,

    /// <summary>Existing path's content was updated ('U' row prefix).</summary>
    Updated = 3,

    /// <summary>Path was newly created ('C' row prefix).</summary>
    Created = 4
  }
}
