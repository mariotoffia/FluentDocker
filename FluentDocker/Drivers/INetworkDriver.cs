#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Provides operations for managing Docker and Podman networks, including creation,
  /// removal, listing, inspection, and container connectivity.
  /// </summary>
  public interface INetworkDriver
  {
    /// <summary>
    /// Creates a new network with the specified configuration.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="config">The network creation configuration including name, driver, and subnet settings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a <see cref="NetworkCreateResult"/>
    /// with the created network's ID and any warnings.
    /// </returns>
    Task<CommandResponse<NetworkCreateResult>> CreateAsync(
        DriverContext context,
        NetworkCreateConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a network by its ID or name. The network must have no connected containers.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="networkId">The ID or name of the network to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> with <see cref="Unit"/> indicating success or failure.
    /// </returns>
    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string networkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists networks, optionally filtered by name or labels.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="filter">
    /// Optional filter criteria to narrow results by name or labels. Pass <c>null</c> to list all networks.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a list of <see cref="Network"/> objects
    /// matching the filter criteria.
    /// </returns>
    Task<CommandResponse<IList<Network>>> ListAsync(
        DriverContext context,
        NetworkListFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects a running or stopped container to a network, enabling it to communicate
    /// with other containers on the same network.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="networkId">The ID or name of the network to connect to.</param>
    /// <param name="containerId">The ID or name of the container to connect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> with <see cref="Unit"/> indicating success or failure.
    /// </returns>
    Task<CommandResponse<Unit>> ConnectAsync(
        DriverContext context,
        string networkId,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects a container from a network.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="networkId">The ID or name of the network to disconnect from.</param>
    /// <param name="containerId">The ID or name of the container to disconnect.</param>
    /// <param name="force">
    /// When <c>true</c>, forcefully disconnects the container even if it is running.
    /// Defaults to <c>false</c>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> with <see cref="Unit"/> indicating success or failure.
    /// </returns>
    Task<CommandResponse<Unit>> DisconnectAsync(
        DriverContext context,
        string networkId,
        string containerId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves detailed information about a specific network, including its configuration,
    /// connected containers, and IPAM settings.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="networkId">The ID or name of the network to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a <see cref="Network"/> with the
    /// network's inspect details. Docker CLI inspect output reports IPv6 under
    /// <c>EnableIPv6</c>; implementations map it to <see cref="Network.IPv6"/> when present.
    /// </returns>
    Task<CommandResponse<Network>> InspectAsync(
        DriverContext context,
        string networkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all unused networks that are not referenced by any container. Built-in
    /// networks (bridge, host, none) are never pruned.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a <see cref="NetworkPruneResult"/>
    /// with the names or IDs of the deleted networks.
    /// </returns>
    Task<CommandResponse<NetworkPruneResult>> PruneAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Configuration for creating a Docker or Podman network.
  /// </summary>
  public class NetworkCreateConfig
  {
    /// <summary>
    /// The name to assign to the network. Must be unique on the host.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The network driver to use (e.g., "bridge", "overlay", "macvlan").
    /// Defaults to "bridge".
    /// </summary>
    public string Driver { get; set; } = "bridge";

    /// <summary>
    /// Driver-specific options as key-value pairs. The available options depend on the
    /// selected <see cref="Driver"/>.
    /// </summary>
    public Dictionary<string, string> Options { get; set; } = [];

    /// <summary>
    /// The subnet for the network in CIDR notation (e.g., "172.28.0.0/16").
    /// When <c>null</c>, the engine assigns a subnet automatically.
    /// </summary>
    public string? Subnet { get; set; }

    /// <summary>
    /// The gateway address for the network (e.g., "172.28.0.1").
    /// When <c>null</c>, the engine assigns a gateway automatically.
    /// </summary>
    public string? Gateway { get; set; }

    /// <summary>
    /// The IP range for automatic container allocation in CIDR notation.
    /// </summary>
    public string IpRange { get; set; }

    /// <summary>
    /// When <c>true</c>, enables IPv6 networking on this network.
    /// </summary>
    public bool EnableIPv6 { get; set; }

    /// <summary>
    /// When <c>true</c>, restricts the network to internal-only traffic, preventing
    /// containers on this network from accessing external networks.
    /// </summary>
    public bool Internal { get; set; }

    /// <summary>
    /// User-defined labels to attach to the network as key-value metadata.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = [];
  }

  /// <summary>
  /// Contains the result of a network creation operation, including the assigned network
  /// ID and any warnings produced during creation.
  /// </summary>
  public class NetworkCreateResult
  {
    /// <summary>
    /// The unique identifier assigned to the created network.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Any warnings generated during network creation (e.g., deprecated options).
    /// </summary>
    public List<string> Warnings { get; set; } = [];
  }

  /// <summary>
  /// Filter criteria for listing networks. All specified filters are combined with AND logic.
  /// </summary>
  public class NetworkListFilter
  {
    /// <summary>
    /// Filters networks by name. Supports partial matching depending on the driver implementation.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Filters networks by labels. Only networks that have all specified key-value pairs are returned.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = [];
  }

  /// <summary>
  /// Represents a Docker or Podman network with its configuration and metadata.
  /// This <c>FluentDocker.Drivers</c> type is the canonical port entity for network data crossing
  /// the driver boundary; it is distinct from adapter-specific parsing models.
  /// </summary>
  public class Network
  {
    /// <summary>
    /// The unique identifier of the network (typically a SHA256 hash or truncated form).
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The human-readable name of the network.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The network driver in use (e.g., "bridge", "overlay", "macvlan").
    /// </summary>
    public string Driver { get; set; }

    /// <summary>
    /// The scope of the network. Typical values are "local" for single-host networks,
    /// "swarm" for Swarm-managed networks, or "global" for multi-host networks.
    /// </summary>
    public string Scope { get; set; }

    /// <summary>
    /// Indicates whether IPv6 is enabled on this network.
    /// </summary>
    [JsonConverter(typeof(LenientBoolConverter))]
    public bool IPv6 { get; set; }

    /// <summary>
    /// Docker inspect alias for <see cref="IPv6"/>. Docker network list uses
    /// <c>IPv6</c>, while inspect uses <c>EnableIPv6</c>.
    /// </summary>
    [JsonPropertyName("EnableIPv6")]
    [JsonConverter(typeof(LenientBoolConverter))]
    public bool EnableIPv6
    {
      get => IPv6;
      set => IPv6 = value;
    }

    /// <summary>
    /// Indicates whether the network is restricted to internal-only traffic,
    /// preventing external access.
    /// </summary>
    [JsonConverter(typeof(LenientBoolConverter))]
    public bool Internal { get; set; }

    /// <summary>
    /// User-defined labels attached to the network as key-value metadata.
    /// Docker CLI returns labels as a comma-separated string (e.g., "key1=val1,key2=val2")
    /// while the API returns a JSON object; the converter handles both formats.
    /// </summary>
    [JsonConverter(typeof(LabelsConverter))]
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>
    /// Containers connected to this network, keyed by container ID as returned by
    /// Docker/Podman network inspect.
    /// </summary>
    public Dictionary<string, NetworkedContainer> Containers { get; set; } = [];
  }

  /// <summary>
  /// Handles deserialization of labels from both comma-separated strings (CLI) and
  /// JSON objects (API).
  /// </summary>
  internal sealed class LabelsConverter : JsonConverter<Dictionary<string, string>>
  {
    public override Dictionary<string, string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.StartObject)
        return JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options)
               ?? [];

      var result = new Dictionary<string, string>();
      if (reader.TokenType != JsonTokenType.String)
      {
        reader.Skip();
        return result;
      }

      var str = reader.GetString();
      if (string.IsNullOrEmpty(str))
        return result;

      // Label VALUES may themselves contain commas (e.g. "desc=a,b"), so a comma-split
      // segment that does not start a new "key=value" pair is a continuation of the previous
      // pair's value and is rejoined with ','. An empty label key is never valid, so a segment
      // whose first char is '=' (eqIdx == 0) is also a continuation (e.g. "v1,=w"), not a new
      // pair. Well-formed "k1=v1,k2=v2" lists parse exactly as before.
      string currentKey = null;
      foreach (var pair in str.Split(','))
      {
        var eqIdx = pair.IndexOf('=');
        if (eqIdx > 0)
        {
          currentKey = pair[..eqIdx];
          result[currentKey] = pair[(eqIdx + 1)..];
        }
        else if (currentKey != null)
        {
          result[currentKey] = result[currentKey] + "," + pair;
        }
      }

      return result;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
      JsonSerializer.Serialize(writer, value, options);
    }
  }

  /// <summary>
  /// Contains the result of a network prune operation, listing all networks that were removed.
  /// </summary>
  public class NetworkPruneResult
  {
    /// <summary>
    /// The names or IDs of networks that were deleted during the prune operation.
    /// </summary>
    public List<string> NetworksDeleted { get; set; } = [];
  }
}
