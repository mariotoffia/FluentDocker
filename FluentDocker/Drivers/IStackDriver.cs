using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Stack management for orchestrated deployments (Docker Swarm, Kubernetes).
  /// Supported by: Docker Swarm, Kubernetes (partial)
  /// Not supported by: Podman (use pods instead)
  /// </summary>
  public interface IStackDriver
  {
    /// <summary>
    /// Lists stacks.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="filter">Optional filter parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of stacks</returns>
    Task<CommandResponse<IList<StackInfo>>> ListAsync(
        DriverContext context,
        StackListFilter filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists tasks in a stack.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="stackName">Stack name</param>
    /// <param name="filter">Optional filter parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tasks</returns>
    Task<CommandResponse<IList<StackTask>>> GetTasksAsync(
        DriverContext context,
        string stackName,
        StackTaskFilter filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deploys a stack from a compose file.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="config">Deploy configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deploy result</returns>
    Task<CommandResponse<StackDeployResult>> DeployAsync(
        DriverContext context,
        StackDeployConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes stacks.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="stackNames">Stack names to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string[] stackNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists services in a stack.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="stackName">Stack name</param>
    /// <param name="filter">Optional filter parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of services</returns>
    Task<CommandResponse<IList<StackServiceInfo>>> GetServicesAsync(
        DriverContext context,
        string stackName,
        StackServiceFilter filter = null,
        CancellationToken cancellationToken = default);
  }

  #region Info Types

  /// <summary>
  /// Represents a stack.
  /// </summary>
  public class StackInfo
  {
    /// <summary>Stack name.</summary>
    public string Name { get; set; }

    /// <summary>Number of services in the stack.</summary>
    public int Services { get; set; }

    /// <summary>Orchestrator (swarm, kubernetes).</summary>
    public string Orchestrator { get; set; }

    /// <summary>Namespace (for Kubernetes).</summary>
    public string Namespace { get; set; }
  }

  /// <summary>
  /// Represents a task in a stack.
  /// </summary>
  public class StackTask
  {
    /// <summary>Task ID.</summary>
    public string Id { get; set; }

    /// <summary>Task name.</summary>
    public string Name { get; set; }

    /// <summary>Image used.</summary>
    public string Image { get; set; }

    /// <summary>Node the task is running on.</summary>
    public string Node { get; set; }

    /// <summary>Desired state.</summary>
    public string DesiredState { get; set; }

    /// <summary>Current state.</summary>
    public string CurrentState { get; set; }

    /// <summary>Error message if any.</summary>
    public string Error { get; set; }

    /// <summary>Ports exposed.</summary>
    public string Ports { get; set; }
  }

  /// <summary>
  /// Represents a service in a stack.
  /// </summary>
  public class StackServiceInfo
  {
    /// <summary>Service ID.</summary>
    public string Id { get; set; }

    /// <summary>Service name.</summary>
    public string Name { get; set; }

    /// <summary>Service mode (replicated, global).</summary>
    public string Mode { get; set; }

    /// <summary>Replicas status (e.g., "3/3").</summary>
    public string Replicas { get; set; }

    /// <summary>Image used.</summary>
    public string Image { get; set; }

    /// <summary>Ports exposed.</summary>
    public string Ports { get; set; }
  }

  #endregion

  #region Config Types

  /// <summary>
  /// Filter for listing stacks.
  /// </summary>
  public class StackListFilter
  {
    /// <summary>Orchestrator to filter by (swarm, kubernetes, all).</summary>
    public string Orchestrator { get; set; }

    /// <summary>Kubernetes namespace.</summary>
    public string Namespace { get; set; }

    /// <summary>Include all namespaces.</summary>
    public bool AllNamespaces { get; set; }

    /// <summary>Kubernetes config file path.</summary>
    public string KubeConfig { get; set; }
  }

  /// <summary>
  /// Filter for listing stack tasks.
  /// </summary>
  public class StackTaskFilter
  {
    /// <summary>Filter by task ID.</summary>
    public string Id { get; set; }

    /// <summary>Filter by task name.</summary>
    public string Name { get; set; }

    /// <summary>Filter by node.</summary>
    public string Node { get; set; }

    /// <summary>Filter by desired state.</summary>
    public string DesiredState { get; set; }

    /// <summary>Don't truncate output.</summary>
    public bool NoTrunc { get; set; }

    /// <summary>Don't resolve IDs to names.</summary>
    public bool NoResolve { get; set; }

    /// <summary>Only display task IDs.</summary>
    public bool Quiet { get; set; }

    /// <summary>Output format.</summary>
    public string Format { get; set; }

    /// <summary>Orchestrator.</summary>
    public string Orchestrator { get; set; }

    /// <summary>Kubernetes namespace.</summary>
    public string Namespace { get; set; }

    /// <summary>Kubernetes config file path.</summary>
    public string KubeConfig { get; set; }
  }

  /// <summary>
  /// Filter for listing stack services.
  /// </summary>
  public class StackServiceFilter
  {
    /// <summary>Filter by service ID.</summary>
    public string Id { get; set; }

    /// <summary>Filter by service name.</summary>
    public string Name { get; set; }

    /// <summary>Filter by label.</summary>
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

    /// <summary>Only display service IDs.</summary>
    public bool Quiet { get; set; }

    /// <summary>Output format.</summary>
    public string Format { get; set; }

    /// <summary>Orchestrator.</summary>
    public string Orchestrator { get; set; }

    /// <summary>Kubernetes namespace.</summary>
    public string Namespace { get; set; }

    /// <summary>Kubernetes config file path.</summary>
    public string KubeConfig { get; set; }
  }

  /// <summary>
  /// Configuration for deploying a stack.
  /// </summary>
  public class StackDeployConfig
  {
    /// <summary>Stack name.</summary>
    public string StackName { get; set; }

    /// <summary>Compose file paths.</summary>
    public List<string> ComposeFiles { get; set; } = new List<string>();

    /// <summary>Orchestrator (swarm, kubernetes).</summary>
    public string Orchestrator { get; set; }

    /// <summary>Kubernetes namespace.</summary>
    public string Namespace { get; set; }

    /// <summary>Kubernetes config file path.</summary>
    public string KubeConfig { get; set; }

    /// <summary>Prune services no longer in compose file.</summary>
    public bool Prune { get; set; }

    /// <summary>Send registry auth details to swarm agents.</summary>
    public bool WithRegistryAuth { get; set; }

    /// <summary>Resolve image digests.</summary>
    public bool ResolveImage { get; set; }

    /// <summary>Environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
  }

  #endregion

  #region Result Types

  /// <summary>
  /// Result of a stack deploy operation.
  /// </summary>
  public class StackDeployResult
  {
    /// <summary>Stack name.</summary>
    public string StackName { get; set; }

    /// <summary>Services created.</summary>
    public List<string> ServicesCreated { get; set; } = new List<string>();

    /// <summary>Services updated.</summary>
    public List<string> ServicesUpdated { get; set; } = new List<string>();

    /// <summary>Networks created.</summary>
    public List<string> NetworksCreated { get; set; } = new List<string>();

    /// <summary>Warnings from the operation.</summary>
    public List<string> Warnings { get; set; } = new List<string>();
  }

  #endregion
}

