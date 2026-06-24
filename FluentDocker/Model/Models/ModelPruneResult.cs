using System.Collections.Generic;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// The result of removing models from the local store (<c>docker model purge</c>).
  /// </summary>
  public sealed class ModelPruneResult
  {
    /// <summary>References of removed models.</summary>
    public IReadOnlyList<string> Removed { get; init; }

    /// <summary>The reclaimed space in bytes (best-effort; 0 when not parseable).</summary>
    public long ReclaimedBytes { get; init; }

    /// <summary>
    /// The raw, unparsed prune output, preserved verbatim. The CLI output format is
    /// not guaranteed across DMR versions, so this is the source of truth when
    /// <see cref="Removed"/> / <see cref="ReclaimedBytes"/> could not be parsed.
    /// </summary>
    public string RawOutput { get; init; }
  }
}
