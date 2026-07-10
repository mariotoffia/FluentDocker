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
      for (var i = 0; i < _operations.Count; i++)
      {
        var candidate = _operations[i];
        if (string.Equals(candidate.ResourceKind, kind, StringComparison.Ordinal) &&
            string.Equals(candidate.ResourceName, name, StringComparison.Ordinal) &&
            candidate.Kernel == kernel &&
            string.Equals(candidate.DriverId, driverId, StringComparison.Ordinal))
          return i;
      }

      return -1;
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
      throw new InvalidOperationException(
          $"container '{containerName}' references {referencedKind} '{reference}' which is {reason}; " +
          "declare dependencies before containers that use them");
    }
  }
}
