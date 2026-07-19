#nullable enable
namespace FluentDocker.Model.Stacks
{
  /// <summary>
  /// The orchestrator a stack is deployed to/queried from (Docker CLI's <c>--orchestrator</c> value).
  /// </summary>
  public enum Orchestrator
  {
    /// <summary>
    /// Unknown orchestrator.
    /// </summary>
    Unknown,

    /// <summary>
    /// All orchestrator.
    /// </summary>
    All,
    /// <summary>
    /// Docker Swarm
    /// </summary>
    Swarm,
    /// <summary>
    /// Kubernetes
    /// </summary>
    Kubernetes
  }
}
