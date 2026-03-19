using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Model.Containers;
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

    public static ContainerState ParseContainerState(JsonElement? stateToken)
        => PodmanContainerParser.ParseContainerState(stateToken);

    public static Health ParseHealth(JsonElement? healthToken)
        => PodmanContainerParser.ParseHealth(healthToken);

    public static ContainerConfig ParseContainerConfig(JsonElement? configToken)
        => PodmanContainerParser.ParseContainerConfig(configToken);

    public static ContainerMount[] ParseMounts(JsonElement? mountsToken)
        => PodmanContainerParser.ParseMounts(mountsToken);

    public static ContainerNetworkSettings ParseNetworkSettings(JsonElement? nsToken)
        => PodmanContainerParser.ParseNetworkSettings(nsToken);

    public static Dictionary<string, HostIpEndpoint[]> ParsePorts(JsonElement? portsToken)
        => PodmanContainerParser.ParsePorts(portsToken);

    public static Dictionary<string, BridgeNetwork> ParseNetworks(JsonElement? networksToken)
        => PodmanContainerParser.ParseNetworks(networksToken);

    public static string[] ParseStringOrArray(JsonElement? token)
        => PodmanContainerParser.ParseStringOrArray(token);
  }
}
