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
    IPodBuilder WithName(string name);

    /// <summary>
    /// Adds a port mapping to the pod (e.g., "8080:80" or "8080").
    /// </summary>
    IPodBuilder WithPort(string hostPort, string containerPort);

    /// <summary>
    /// Exposes a container port, letting the runtime assign a random host port.
    /// </summary>
    IPodBuilder ExposePort(string containerPort);

    /// <summary>
    /// Connects the pod to a network.
    /// </summary>
    IPodBuilder WithNetwork(string networkName);

    /// <summary>
    /// Adds a label to the pod.
    /// </summary>
    IPodBuilder WithLabel(string key, string value);

    /// <summary>
    /// Sets the hostname of the pod.
    /// </summary>
    IPodBuilder WithHostname(string hostname);

    /// <summary>
    /// Remove the pod on dispose.
    /// </summary>
    IPodBuilder RemoveOnDispose();
  }
}
