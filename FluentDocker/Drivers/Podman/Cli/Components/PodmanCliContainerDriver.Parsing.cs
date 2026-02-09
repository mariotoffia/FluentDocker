using System.Collections.Generic;
using FluentDocker.Model.Containers;
using Newtonsoft.Json.Linq;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  public partial class PodmanCliContainerDriver
  {
    // Parsing methods have been moved to PodmanContainerParser.
    // These delegating methods are kept for backward compatibility during the v3.0 cycle.

    public static IList<Container> ParseContainerList(string json)
        => PodmanContainerParser.ParseContainerList(json);

    public static Container ParseContainerInspect(string json)
        => PodmanContainerParser.ParseContainerInspect(json);

    public static ContainerState ParseContainerState(JToken stateToken)
        => PodmanContainerParser.ParseContainerState(stateToken);

    public static Health ParseHealth(JToken healthToken)
        => PodmanContainerParser.ParseHealth(healthToken);

    public static ContainerConfig ParseContainerConfig(JToken configToken)
        => PodmanContainerParser.ParseContainerConfig(configToken);

    public static ContainerMount[] ParseMounts(JToken mountsToken)
        => PodmanContainerParser.ParseMounts(mountsToken);

    public static ContainerNetworkSettings ParseNetworkSettings(JToken nsToken)
        => PodmanContainerParser.ParseNetworkSettings(nsToken);

    public static Dictionary<string, HostIpEndpoint[]> ParsePorts(JToken portsToken)
        => PodmanContainerParser.ParsePorts(portsToken);

    public static Dictionary<string, BridgeNetwork> ParseNetworks(JToken networksToken)
        => PodmanContainerParser.ParseNetworks(networksToken);

    public static string[] ParseStringOrArray(JToken token)
        => PodmanContainerParser.ParseStringOrArray(token);
  }
}
