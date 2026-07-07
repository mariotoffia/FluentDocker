#nullable enable
using System.Collections.Generic;

namespace FluentDocker.Kernel
{
  /// <summary>
  /// Programmatic summary of resources observed during a failed build cleanup.
  /// </summary>
  public sealed class BuildFailureManifest(
      IEnumerable<BuildFailureResource> removedResources,
      IEnumerable<BuildFailureResource> keptResources)
  {
    /// <summary>Resources cleanup removed after the build failed.</summary>
    public IReadOnlyList<BuildFailureResource> RemovedResources { get; } =
        [.. removedResources ?? []];

    /// <summary>Resources intentionally kept or left because cleanup failed.</summary>
    public IReadOnlyList<BuildFailureResource> KeptResources { get; } =
        [.. keptResources ?? []];
  }

  /// <summary>
  /// A resource entry in a build failure manifest.
  /// </summary>
  public sealed record BuildFailureResource(
      string Kind,
      string? Name,
      string? Id,
      string Reason);
}
