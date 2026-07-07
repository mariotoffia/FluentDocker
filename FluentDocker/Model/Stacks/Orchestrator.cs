#nullable enable
namespace FluentDocker.Model.Stacks
{
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
