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

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseContainerList(string)"/>.</summary>
    /// <param name="json">Raw stdout from <c>podman ps --format json</c>.</param>
    /// <returns>One container per list entry; an empty list when <paramref name="json"/> is null/blank.</returns>
    public static IList<Container> ParseContainerList(string json)
        => PodmanContainerParser.ParseContainerList(json);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseContainerInspect(string)"/>.</summary>
    /// <param name="json">Raw stdout from <c>podman inspect</c>. Must not be null.</param>
    /// <returns>The parsed container, with nested state/config/mounts/network settings resolved.</returns>
    public static Container ParseContainerInspect(string json)
        => PodmanContainerParser.ParseContainerInspect(json);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseContainerState(JsonElement?)"/>.</summary>
    /// <param name="stateToken">The <c>State</c> property value, or null if absent.</param>
    /// <returns>The parsed state; a default empty instance when <paramref name="stateToken"/> is null/JSON-null.</returns>
    public static ContainerState ParseContainerState(JsonElement? stateToken)
        => PodmanContainerParser.ParseContainerState(stateToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseHealth(JsonElement?)"/>.</summary>
    /// <param name="healthToken">The <c>Health</c>/<c>Healthcheck</c> property value, or null.</param>
    /// <returns>The parsed health; <c>null</c> when <paramref name="healthToken"/> is null/JSON-null.</returns>
    public static Health ParseHealth(JsonElement? healthToken)
        => PodmanContainerParser.ParseHealth(healthToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseContainerConfig(JsonElement?)"/>.</summary>
    /// <param name="configToken">The <c>Config</c> property value, or null if absent.</param>
    /// <returns>The parsed config; <c>null</c> when <paramref name="configToken"/> is null/JSON-null.</returns>
    public static ContainerConfig ParseContainerConfig(JsonElement? configToken)
        => PodmanContainerParser.ParseContainerConfig(configToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseMounts(JsonElement?)"/>.</summary>
    /// <param name="mountsToken">The <c>Mounts</c> property value, or null if absent.</param>
    /// <returns>One mount per array entry; an empty array when <paramref name="mountsToken"/> is null/not-an-array.</returns>
    public static ContainerMount[] ParseMounts(JsonElement? mountsToken)
        => PodmanContainerParser.ParseMounts(mountsToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseNetworkSettings(JsonElement?)"/>.</summary>
    /// <param name="nsToken">The <c>NetworkSettings</c> property value, or null if absent.</param>
    /// <returns>The parsed settings; <c>null</c> when <paramref name="nsToken"/> is null/JSON-null.</returns>
    public static ContainerNetworkSettings ParseNetworkSettings(JsonElement? nsToken)
        => PodmanContainerParser.ParseNetworkSettings(nsToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParsePorts(JsonElement?)"/>.</summary>
    /// <param name="portsToken">The <c>Ports</c> property value, or null if absent.</param>
    /// <returns>Port-spec to host-binding map; <c>null</c> when <paramref name="portsToken"/> is null/not-an-object.</returns>
    public static Dictionary<string, HostIpEndpoint[]> ParsePorts(JsonElement? portsToken)
        => PodmanContainerParser.ParsePorts(portsToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseNetworks(JsonElement?)"/>.</summary>
    /// <param name="networksToken">The <c>Networks</c> property value, or null if absent.</param>
    /// <returns>Network-name to endpoint map; <c>null</c> when <paramref name="networksToken"/> is null/not-an-object.</returns>
    public static Dictionary<string, BridgeNetwork> ParseNetworks(JsonElement? networksToken)
        => PodmanContainerParser.ParseNetworks(networksToken);

    /// <summary>Delegates to <see cref="PodmanContainerParser.ParseStringOrArray(JsonElement?)"/>.</summary>
    /// <param name="token">The property value, or null if absent.</param>
    /// <returns>Array form as-is; a single string wrapped in an array; otherwise <c>null</c>.</returns>
    public static string[] ParseStringOrArray(JsonElement? token)
        => PodmanContainerParser.ParseStringOrArray(token);
  }
}
