namespace FluentDocker.Model.Models
{
  /// <summary>
  /// Model store disk usage (the result of <c>docker model df</c>).
  /// </summary>
  public sealed class ModelDiskUsage
  {
    /// <summary>Total size of all local models in bytes.</summary>
    public long ModelsSizeBytes { get; init; }

    /// <summary>The number of local models.</summary>
    public int ModelCount { get; init; }

    /// <summary>The reclaimable size in bytes.</summary>
    public long ReclaimableBytes { get; init; }
  }
}
