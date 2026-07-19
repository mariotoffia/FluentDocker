#nullable enable
using System;

namespace FluentDocker.Model.Stacks
{
  /// <summary>
  /// A single row from <c>docker stack ls</c> output.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use IStackDriver list response models instead.")]
  public sealed class StackLsResponse
  {
    /// <summary>Name of the stack.</summary>
    public string? Name { get; set; }

    /// <summary>Number of services in the stack.</summary>
    public int Services { get; set; }

    /// <summary>Orchestrator the stack is deployed to.</summary>
    public Orchestrator Orchestrator { get; set; }

    /// <summary>Kubernetes namespace the stack is deployed to; only meaningful for the Kubernetes orchestrator.</summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Parses the <c>ORCHESTRATOR</c> column text from <c>docker stack ls</c> into an <see cref="Stacks.Orchestrator"/>.
    /// </summary>
    /// <param name="value">The raw orchestrator column value (e.g. "swarm", "kubernetes").</param>
    /// <returns>
    /// <see cref="Orchestrator.All"/> when <paramref name="value"/> is empty, the matching orchestrator for
    /// "swarm"/"kubernetes" (case-insensitive), otherwise <see cref="Orchestrator.Unknown"/>.
    /// </returns>
    public static Orchestrator ToOrchestrator(string value)
    {
      if (string.IsNullOrEmpty(value))
        return Orchestrator.All;

      if (string.Equals(value, "kubernetes", StringComparison.OrdinalIgnoreCase))
        return Orchestrator.Kubernetes;
      return string.Equals(value, "swarm", StringComparison.OrdinalIgnoreCase)
          ? Orchestrator.Swarm
          : Orchestrator.Unknown;
    }
  }
}
