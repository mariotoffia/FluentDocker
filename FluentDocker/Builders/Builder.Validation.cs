using System;
using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Kernel;

namespace FluentDocker.Builders
{
  public partial class Builder
  {
    private void RefreshContainerSnapshots()
    {
      foreach (var operation in _operations)
      {
        if (operation.ResourceBuilder is not ContainerBuilder builder)
          continue;
        operation.ResourceName = builder.ContainerName;
        operation.NetworkReferences = builder.NetworkReferences;
        operation.VolumeReferences = builder.VolumeReferences;
        operation.LinkReferences = builder.LinkReferences;
        operation.ImageReferences = builder.ImageReferences;
        operation.PodReferences = builder.PodReferences;
        // Wait parameters were snapshotted by value at UseContainer time, but StartDeferred is a
        // live lambda and names/references are refreshed here — so a WaitForPort(...)/AllowCleanExit
        // set on the stashed builder AFTER UseContainer would otherwise run the deferred start with
        // the stale budget (BLD-MAJ-3). Refresh them from the builder's current values too.
        operation.AllowCleanExit = builder.AllowCleanExitOnStart;
        operation.StartupTimeoutMs = builder.StartupTimeoutMs;
        operation.StartupPollIntervalMs = builder.StartupPollIntervalMs;
      }
    }

    private void ValidateOperationReferences()
    {
      for (var i = 0; i < _operations.Count; i++)
      {
        var operation = _operations[i];
        if (!string.Equals(operation.ResourceKind, "container", StringComparison.Ordinal))
          continue;

        ValidateReferences(i, operation, "network", operation.NetworkReferences);
        ValidateReferences(i, operation, "volume", operation.VolumeReferences);
        ValidateReferences(i, operation, "container", operation.LinkReferences);
        ValidateReferences(i, operation, "image", operation.ImageReferences);
        ValidateReferences(i, operation, "pod", operation.PodReferences);
      }
    }

    private void ValidateContiguousScopes()
    {
      var closedScopes = new HashSet<(FluentDockerKernel Kernel, string DriverId)>();
      var hasCurrent = false;
      FluentDockerKernel currentKernel = null;
      string currentDriverId = null;

      foreach (var operation in _operations)
      {
        if (hasCurrent &&
            (operation.Kernel != currentKernel ||
             !string.Equals(operation.DriverId, currentDriverId, StringComparison.Ordinal)))
        {
          closedScopes.Add((currentKernel, currentDriverId));
        }

        if (closedScopes.Contains((operation.Kernel, operation.DriverId)))
        {
          throw new FluentDockerException(
              $"operations for driver scope '{operation.DriverId}' are not contiguous; " +
              "declare each scope's operations together or use separate builders");
        }

        currentKernel = operation.Kernel;
        currentDriverId = operation.DriverId;
        hasCurrent = true;
      }
    }

    private void ValidateReferences(
        int operationIndex,
        BuildOperation operation,
        string referencedKind,
        IReadOnlyCollection<string> references)
    {
      foreach (var reference in references)
      {
        // Scope the lookup to the referencing operation's (Kernel, DriverId). A resource of the
        // same name may legitimately exist in another driver scope; only same-scope ordering is
        // validated here. References that resolve outside this scope (or to pre-existing/external
        // resources) are left for the driver to validate at runtime.
        var referencedIndex = FindOperationInScope(
            referencedKind, reference, operation.Kernel, operation.DriverId);
        if (referencedIndex < 0)
          continue;

        if (referencedIndex > operationIndex)
          ThrowReferenceOrderError(operation, referencedKind, reference, "declared after it");
      }
    }

    private int FindOperationInScope(
        string kind, string name, FluentDockerKernel kernel, string driverId)
    {
      var normalizedName = NormalizeReferenceForComparison(kind, name);
      for (var i = 0; i < _operations.Count; i++)
      {
        var candidate = _operations[i];
        if (string.Equals(candidate.ResourceKind, kind, StringComparison.Ordinal) &&
            string.Equals(
                NormalizeReferenceForComparison(kind, candidate.ResourceName),
                normalizedName,
                StringComparison.Ordinal) &&
            candidate.Kernel == kernel &&
            string.Equals(candidate.DriverId, driverId, StringComparison.Ordinal))
          return i;
      }

      return -1;
    }

    /// <summary>
    /// Canonicalizes an image reference for ordering validation so <c>img</c>,
    /// <c>img:latest</c>, and other tag-notation variants of the same image compare equal
    /// (both the referencing container and the referenced image operation are normalized via
    /// <see cref="ContainerBuilder.ParseImageReference"/>). Non-image kinds compare raw.
    /// </summary>
    private static string NormalizeReferenceForComparison(string kind, string name)
    {
      if (!string.Equals(kind, "image", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name))
        return name;

      try
      {
        var (image, tag) = ContainerBuilder.ParseImageReference(name);
        return tag == null ? image : $"{image}:{tag}";
      }
      catch (FluentDockerException)
      {
        // A malformed reference (e.g. empty tag) gets its dedicated error at build time;
        // for ordering purposes compare it verbatim.
        return name;
      }
    }

    private static void ThrowReferenceOrderError(
        BuildOperation operation,
        string referencedKind,
        string reference,
        string reason)
    {
      var containerName = string.IsNullOrWhiteSpace(operation.ResourceName)
          ? "<unnamed>"
          : operation.ResourceName;
      // Graph/config validation uses FluentDockerException uniformly (matching the non-contiguous
      // scope check) so a caller can catch one type for "builder graph misconfigured"; plain
      // InvalidOperationException is reserved for lifecycle misuse (BLD-MAJ-4).
      throw new FluentDockerException(
          $"container '{containerName}' references {referencedKind} '{reference}' which is {reason}; " +
          "declare dependencies before containers that use them");
    }
  }
}
