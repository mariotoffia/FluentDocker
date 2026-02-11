using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Network-specific driver operations.
  /// </summary>
  public interface INetworkDriver
  {
    /// <summary>
    /// Creates a network.
    /// </summary>
    Task<CommandResponse<NetworkCreateResult>> CreateAsync(
        DriverContext context,
        NetworkCreateConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a network.
    /// </summary>
    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string networkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists networks.
    /// </summary>
    Task<CommandResponse<IList<Network>>> ListAsync(
        DriverContext context,
        NetworkListFilter filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects a container to a network.
    /// </summary>
    Task<CommandResponse<Unit>> ConnectAsync(
        DriverContext context,
        string networkId,
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects a container from a network.
    /// </summary>
    Task<CommandResponse<Unit>> DisconnectAsync(
        DriverContext context,
        string networkId,
        string containerId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects a network to get detailed information.
    /// </summary>
    Task<CommandResponse<Network>> InspectAsync(
        DriverContext context,
        string networkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes unused networks.
    /// </summary>
    Task<CommandResponse<NetworkPruneResult>> PruneAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);
  }

  public class NetworkCreateConfig
  {
    public string Name { get; set; }
    public string Driver { get; set; } = "bridge";
    public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    public string Subnet { get; set; }
    public string Gateway { get; set; }
    public bool EnableIPv6 { get; set; }
    public bool Internal { get; set; }
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
  }

  public class NetworkCreateResult
  {
    public string Id { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
  }

  public class NetworkListFilter
  {
    public string Name { get; set; }
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
  }

  public class Network
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Driver { get; set; }
    public string Scope { get; set; }
    public bool IPv6 { get; set; }
    public bool Internal { get; set; }

    /// <summary>
    /// Labels for the network. Docker CLI returns labels as a comma-separated string
    /// (e.g. "key1=val1,key2=val2") while the API returns a JSON object.
    /// </summary>
    [JsonConverter(typeof(LabelsConverter))]
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
  }

  /// <summary>
  /// Handles deserialization of labels from both comma-separated strings (CLI) and
  /// JSON objects (API).
  /// </summary>
  internal class LabelsConverter : JsonConverter<Dictionary<string, string>>
  {
    public override Dictionary<string, string> ReadJson(
        JsonReader reader, Type objectType, Dictionary<string, string> existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
      var token = JToken.Load(reader);
      if (token.Type == JTokenType.Object)
        return token.ToObject<Dictionary<string, string>>();

      var result = new Dictionary<string, string>();
      var str = token.Value<string>();
      if (string.IsNullOrEmpty(str))
        return result;

      foreach (var pair in str.Split(','))
      {
        var eqIdx = pair.IndexOf('=');
        if (eqIdx > 0)
          result[pair[..eqIdx]] = pair[(eqIdx + 1)..];
      }

      return result;
    }

    public override void WriteJson(
        JsonWriter writer, Dictionary<string, string> value, JsonSerializer serializer)
    {
      serializer.Serialize(writer, value);
    }
  }

  public class NetworkPruneResult
  {
    public List<string> NetworksDeleted { get; set; } = new List<string>();
  }
}
