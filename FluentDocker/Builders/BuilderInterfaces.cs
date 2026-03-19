using System.Collections.Generic;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Network builder for lambda configuration.
  /// </summary>
  public interface INetworkBuilder
  {
    /// <summary>Sets the network name.</summary>
    /// <param name="name">Name for the Docker/Podman network.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithName(string name);

    /// <summary>Sets the network driver (e.g. "bridge", "overlay", "macvlan").</summary>
    /// <param name="driver">Driver name. Defaults to "bridge" if not set.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder UseDriver(string driver);

    /// <summary>Sets the subnet in CIDR format (e.g. "172.28.0.0/16").</summary>
    /// <param name="subnet">Subnet CIDR string.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithSubnet(string subnet);

    /// <summary>Sets the gateway address for the network.</summary>
    /// <param name="gateway">Gateway IP address (e.g. "172.28.0.1").</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithGateway(string gateway);

    /// <summary>Sets the IP address range for container allocation.</summary>
    /// <param name="ipRange">IP range in CIDR format.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithIPRange(string ipRange);

    /// <summary>Enables or disables IPv6 on the network.</summary>
    /// <param name="enableIPv6">True to enable IPv6; false to disable.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithIPv6(bool enableIPv6 = true);

    /// <summary>Marks the network as internal (no external connectivity).</summary>
    /// <param name="isInternal">True to restrict the network to internal-only traffic.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder AsInternal(bool isInternal = true);

    /// <summary>Adds a label to the network.</summary>
    /// <param name="key">Label key.</param>
    /// <param name="value">Label value.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithLabel(string key, string value);

    /// <summary>Adds a driver-specific option.</summary>
    /// <param name="key">Option key.</param>
    /// <param name="value">Option value.</param>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder WithOption(string key, string value);

    /// <summary>Remove network on dispose.</summary>
    /// <returns>The builder for fluent chaining.</returns>
    INetworkBuilder RemoveOnDispose();
  }

  /// <summary>
  /// Volume builder for lambda configuration.
  /// </summary>
  public interface IVolumeBuilder
  {
    /// <summary>Sets the volume name.</summary>
    /// <param name="name">Name for the Docker/Podman volume.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IVolumeBuilder WithName(string name);

    /// <summary>Sets the volume driver (e.g. "local", "nfs").</summary>
    /// <param name="driver">Driver name. Defaults to "local" if not set.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IVolumeBuilder UseDriver(string driver);

    /// <summary>Adds a driver-specific option (e.g. mount type, device).</summary>
    /// <param name="key">Option key.</param>
    /// <param name="value">Option value.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IVolumeBuilder WithDriverOption(string key, string value);

    /// <summary>Adds a label to the volume.</summary>
    /// <param name="key">Label key.</param>
    /// <param name="value">Label value.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IVolumeBuilder WithLabel(string key, string value);

    /// <summary>Remove volume on dispose.</summary>
    /// <returns>The builder for fluent chaining.</returns>
    IVolumeBuilder RemoveOnDispose();
  }

  /// <summary>
  /// Compose builder for lambda configuration.
  /// </summary>
  public interface IComposeBuilder
  {
    /// <summary>Adds a compose file to the project configuration.</summary>
    /// <param name="path">Absolute or relative path to the compose YAML file.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithComposeFile(string path);

    /// <summary>Adds multiple compose files for overrides and extensions.</summary>
    /// <param name="paths">Paths to compose YAML files, applied in order.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithComposeFiles(params string[] paths);

    /// <summary>Sets the project name used to prefix container and network names.</summary>
    /// <param name="name">Project name for the compose stack.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithProjectName(string name);

    /// <summary>Sets an environment variable available during compose interpolation.</summary>
    /// <param name="key">Environment variable name.</param>
    /// <param name="value">Environment variable value.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithEnvironment(string key, string value);

    /// <summary>Sets multiple environment variables from a dictionary.</summary>
    /// <param name="environment">Dictionary of environment variable key-value pairs.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithEnvironment(IDictionary<string, string> environment);

    /// <summary>Loads environment variables from an env file for compose interpolation.</summary>
    /// <param name="path">Path to the env file.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithEnvFile(string path);

    /// <summary>Enables or disables building images before starting containers.</summary>
    /// <param name="build">True to build images; false to skip the build step.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithBuild(bool build = true);

    /// <summary>Forces container recreation even if the configuration has not changed.</summary>
    /// <param name="forceRecreate">True to force recreation; false to reuse existing containers.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithForceRecreate(bool forceRecreate = true);

    /// <summary>Removes containers for services not defined in the compose file on up.</summary>
    /// <param name="removeOrphans">True to remove orphaned containers; false to leave them.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithRemoveOrphans(bool removeOrphans = true);

    /// <summary>Restricts compose operations to the specified services only.</summary>
    /// <param name="services">Service names to target.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder ForServices(params string[] services);

    /// <summary>Enables or disables volume removal when the stack is torn down.</summary>
    /// <param name="removeVolumes">True to remove volumes on down; false to preserve them.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithRemoveVolumes(bool removeVolumes = true);

    /// <summary>Enables or disables image removal when the stack is torn down.</summary>
    /// <param name="removeImages">True to remove images on down; false to keep them.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithRemoveImages(bool removeImages = true);

    /// <summary>Sets the timeout for graceful container shutdown.</summary>
    /// <param name="seconds">Shutdown timeout in seconds.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithTimeout(int seconds);

    /// <summary>Scales a service to the specified number of replicas.</summary>
    /// <param name="service">Name of the service to scale.</param>
    /// <param name="replicas">Number of container replicas to run.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithScale(string service, int replicas);

    /// <summary>Enables or disables starting linked/dependent services.</summary>
    /// <param name="noDeps">True to skip starting dependencies; false to start them normally.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithNoDeps(bool noDeps = true);

    /// <summary>Creates containers without starting them, useful for configuration validation.</summary>
    /// <param name="noStart">True to skip starting; false to start normally.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithNoStart(bool noStart = true);

    /// <summary>Enables or disables always pulling images before running.</summary>
    /// <param name="always">True to always pull the latest images; false to use cached images.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithPull(bool always = true);

    /// <summary>Waits for services to become healthy before considering the stack started.</summary>
    /// <param name="wait">True to wait for health checks; false to return immediately.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithWait(bool wait = true);

    /// <summary>Sets the maximum time to wait for services to become healthy.</summary>
    /// <param name="seconds">Wait timeout in seconds.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithWaitTimeout(int seconds);

    /// <summary>Activates the specified compose profiles for selective service inclusion.</summary>
    /// <param name="profiles">Profile names to activate.</param>
    /// <returns>The builder for fluent chaining.</returns>
    IComposeBuilder WithProfiles(params string[] profiles);
  }
}
