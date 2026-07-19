namespace FluentDocker.Builders
{
  /// <summary>
  /// Builder for creating and configuring a Podman pod.
  /// </summary>
  public interface IPodBuilder
  {
    /// <summary>
    /// Sets the pod name.
    /// </summary>
    /// <param name="name">The pod name.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder WithName(string name);

    /// <summary>
    /// Adds a port mapping to the pod (e.g., "8080:80" or "8080").
    /// </summary>
    /// <param name="hostPort">The host port or IP/port binding.</param>
    /// <param name="containerPort">The container port with optional protocol.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder WithPort(string hostPort, string containerPort);

    /// <summary>
    /// Exposes a container port, letting the runtime assign a random host port.
    /// </summary>
    /// <param name="containerPort">The container port with optional protocol.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder ExposePort(string containerPort);

    /// <summary>
    /// Connects the pod to a network.
    /// </summary>
    /// <param name="networkName">The network name.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder WithNetwork(string networkName);

    /// <summary>
    /// Adds a label to the pod.
    /// </summary>
    /// <param name="key">The label key.</param>
    /// <param name="value">The label value.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder WithLabel(string key, string value);

    /// <summary>
    /// Sets the hostname of the pod.
    /// </summary>
    /// <param name="hostname">The pod hostname.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder WithHostname(string hostname);

    /// <summary>
    /// Remove the pod on dispose.
    /// </summary>
    /// <returns>The builder instance for method chaining.</returns>
    IPodBuilder RemoveOnDispose();
  }
}
