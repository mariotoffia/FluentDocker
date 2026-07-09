#nullable enable
using System;

namespace FluentDocker.Model.Stacks
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use IStackDriver list response models instead.")]
  public sealed class StackLsResponse
  {
    public string? Name { get; set; }
    public int Services { get; set; }
    public Orchestrator Orchestrator { get; set; }
    public string? Namespace { get; set; }

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
