using System;
using System.Collections.Generic;
using FluentDocker.Kernel;

namespace FluentDocker.Builders
{
  public partial class Builder
  {
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
