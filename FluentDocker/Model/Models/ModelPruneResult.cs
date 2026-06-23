using System.Collections.Generic;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// The result of pruning unused models (<c>docker model prune</c>).
  /// </summary>
  public sealed class ModelPruneResult
  {
    /// <summary>References of removed models.</summary>
    public IReadOnlyList<string> Removed { get; init; }

    /// <summary>The reclaimed space in bytes.</summary>
    public long ReclaimedBytes { get; init; }
  }
}
