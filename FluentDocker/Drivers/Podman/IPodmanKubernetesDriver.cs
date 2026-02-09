using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman
{
  /// <summary>
  /// Podman-specific driver interface for Kubernetes YAML operations.
  /// Supports deploying K8s manifests via <c>podman kube play</c>,
  /// tearing down via <c>podman kube down</c>, and generating YAML
  /// via <c>podman kube generate</c>.
  /// </summary>
  public interface IPodmanKubernetesDriver
  {
    /// <summary>
    /// Deploys resources from a Kubernetes YAML file.
    /// Equivalent to <c>podman kube play [flags] file.yaml</c>.
    /// </summary>
    Task<CommandResponse<KubePlayResult>> PlayAsync(
        DriverContext context, KubePlayConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears down resources previously deployed from a Kubernetes YAML file.
    /// Equivalent to <c>podman kube down file.yaml</c>.
    /// </summary>
    Task<CommandResponse<Unit>> DownAsync(
        DriverContext context, string yamlPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates Kubernetes YAML from an existing pod or container.
    /// Equivalent to <c>podman kube generate resource-name</c>.
    /// </summary>
    Task<CommandResponse<string>> GenerateAsync(
        DriverContext context, string resourceName,
        CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Configuration for <c>podman kube play</c>.
  /// </summary>
  public class KubePlayConfig
  {
    /// <summary>Path to the Kubernetes YAML file (required).</summary>
    public string YamlPath { get; set; }

    /// <summary>Network to connect pods to (--network).</summary>
    public string Network { get; set; }

    /// <summary>Path(s) to ConfigMap YAML files (--configmap).</summary>
    public List<string> ConfigMaps { get; set; } = new List<string>();

    /// <summary>Log driver for containers (--log-driver).</summary>
    public string LogDriver { get; set; }

    /// <summary>Replace existing pods/containers if they exist (--replace).</summary>
    public bool Replace { get; set; }

    /// <summary>
    /// Whether to start the pod after creation. Defaults to true.
    /// Set to false to add <c>--start=false</c>.
    /// </summary>
    public bool Start { get; set; } = true;

    /// <summary>Annotations to add to pods (--annotation key=value).</summary>
    public Dictionary<string, string> Annotations { get; set; }
        = new Dictionary<string, string>();
  }

  /// <summary>
  /// Result of a <c>podman kube play</c> operation.
  /// </summary>
  public class KubePlayResult
  {
    /// <summary>Pods created by the play operation.</summary>
    public IList<KubePlayPodResult> Pods { get; set; } = new List<KubePlayPodResult>();
  }

  /// <summary>
  /// Information about a pod created by <c>podman kube play</c>.
  /// </summary>
  public class KubePlayPodResult
  {
    /// <summary>Pod identifier.</summary>
    public string Id { get; set; }

    /// <summary>Container identifiers within this pod.</summary>
    public IList<string> Containers { get; set; } = new List<string>();
  }
}
