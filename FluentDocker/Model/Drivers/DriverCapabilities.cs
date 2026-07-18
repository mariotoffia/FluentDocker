#nullable enable
namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Describes the capabilities supported by a driver.
  /// </summary>
  public class DriverCapabilities
  {
    /// <summary>
    /// Driver supports container operations.
    /// </summary>
    public bool SupportsContainers { get; set; }

    /// <summary>
    /// Driver supports image operations.
    /// </summary>
    public bool SupportsImages { get; set; }

    /// <summary>
    /// Driver supports network operations.
    /// </summary>
    public bool SupportsNetworks { get; set; }

    /// <summary>
    /// Driver supports volume operations.
    /// </summary>
    public bool SupportsVolumes { get; set; }

    /// <summary>
    /// Driver supports compose operations.
    /// </summary>
    public bool SupportsCompose { get; set; }

    /// <summary>
    /// Driver supports system operations (info, version, events).
    /// </summary>
    public bool SupportsSystem { get; set; }

    /// <summary>
    /// Driver supports pod operations (Podman-specific).
    /// </summary>
    public bool SupportsPods { get; set; }

    /// <summary>
    /// Driver supports Kubernetes YAML operations (Podman kube play/down/generate).
    /// </summary>
    public bool SupportsKubernetes { get; set; }

    /// <summary>
    /// Driver supports machine management (Podman machine init/start/stop/etc.).
    /// </summary>
    /// <remarks>
    /// The Podman CLI pack reports <c>true</c> on every platform because <c>podman machine</c>
    /// exists on Linux too. The library's machine <b>auto-start</b> is narrower: it only runs
    /// on macOS/Windows; on native Linux a machine must be started externally.
    /// </remarks>
    public bool SupportsMachines { get; set; }

    /// <summary>
    /// Driver supports manifest/multi-arch operations (Podman-specific).
    /// </summary>
    public bool SupportsManifests { get; set; }

    /// <summary>
    /// Driver supports Swarm stack operations (docker stack deploy/rm/ls).
    /// </summary>
    public bool SupportsStacks { get; set; }

    /// <summary>
    /// Driver supports Swarm service operations (docker service create/inspect/scale).
    /// </summary>
    public bool SupportsServices { get; set; }

    /// <summary>
    /// Driver supports Docker Model Runner operations.
    /// </summary>
    public bool SupportsModels { get; set; }

    /// <summary>
    /// Driver version string.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// API version (if applicable).
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Creates conservative default capabilities: core container, image, network, volume,
    /// compose, and system operations are supported; runtime-specific capabilities default to false.
    /// </summary>
    public static DriverCapabilities Default()
    {
      return new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsImages = true,
        SupportsNetworks = true,
        SupportsVolumes = true,
        SupportsCompose = true,
        SupportsSystem = true,
        SupportsPods = false,
        SupportsKubernetes = false,
        SupportsMachines = false,
        SupportsManifests = false,
        SupportsStacks = false,
        SupportsServices = false,
        SupportsModels = false
      };
    }
  }
}
