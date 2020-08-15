namespace Ductus.FluentDocker.Model.Stacks
{
  public enum Orchestrator
  {
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
    Kubernetes,
    /// <summary>
    /// No orchestrator.
    /// </summary>
    None
  }
}
