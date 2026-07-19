#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of INetworkDriver.
  /// Uses /networks endpoints.
  /// </summary>
  public class DockerApiNetworkDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection), INetworkDriver
  {
    /// <inheritdoc />
    public async Task<CommandResponse<NetworkCreateResult>> CreateAsync(
        DriverContext context, NetworkCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var body = new Dictionary<string, object>
      {
        ["Name"] = config.Name,
        ["Driver"] = config.Driver ?? "bridge",
        ["Internal"] = config.Internal,
        ["EnableIPv6"] = config.EnableIPv6,
      };

      if (config.Labels?.Count > 0)
        body["Labels"] = config.Labels;

      if (config.Options?.Count > 0)
        body["Options"] = config.Options;

      if (!string.IsNullOrEmpty(config.Subnet) ||
          !string.IsNullOrEmpty(config.Gateway) ||
          !string.IsNullOrEmpty(config.IpRange))
      {
        var ipamConfig = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(config.Subnet))
          ipamConfig["Subnet"] = config.Subnet;
        if (!string.IsNullOrEmpty(config.Gateway))
          ipamConfig["Gateway"] = config.Gateway;
        if (!string.IsNullOrEmpty(config.IpRange))
          ipamConfig["IPRange"] = config.IpRange;

        body["IPAM"] = new Dictionary<string, object>
        {
          ["Config"] = new[] { ipamConfig }
        };
      }

      var result = await PostJsonElementAsync("/networks/create", body, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<NetworkCreateResult>.Fail(result.ErrorMessage,
            result.StatusCode is 599 or 408
                ? MapHttpErrorCode(result.StatusCode)
                : ErrorCodes.Network.CreateFailed,
            CreateErrorContext("POST /networks/create", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var data = result.Data;
      var createResult = new NetworkCreateResult
      {
        Id = data.GetStringOrDefault("Id")
      };

      var warningEl = data.Prop("Warning");
      if (warningEl != null && warningEl.Value.ValueKind == JsonValueKind.String)
        createResult.Warnings.Add(warningEl.Value.GetString());

      return CommandResponse<NetworkCreateResult>.Ok(createResult);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string networkId,
        CancellationToken cancellationToken = default)
    {
      var result = await DeleteAsync($"/networks/{Uri.EscapeDataString(networkId)}", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Network.NotFound),
            CreateErrorContext($"DELETE /networks/{networkId}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Network>>> ListAsync(
        DriverContext context, NetworkListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      var path = "/networks";
      if (filter?.Name != null)
      {
        var filters = JsonHelper.Serialize(
            new Dictionary<string, string[]> { ["name"] = [filter.Name] });
        path += $"?filters={Uri.EscapeDataString(filters)}";
      }

      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<Network>>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /networks", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var networks = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseNetwork).ToList()
          : [];
      return CommandResponse<IList<Network>>.Ok(networks);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ConnectAsync(
        DriverContext context, string networkId, string containerId,
        CancellationToken cancellationToken = default)
    {
      var body = new { Container = containerId };
      var result = await PostAsync(
          $"/networks/{Uri.EscapeDataString(networkId)}/connect", body, cancellationToken)
          .ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            ErrorCodes.Network.ConnectFailed,
            CreateErrorContext($"POST /networks/{networkId}/connect", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> DisconnectAsync(
        DriverContext context, string networkId, string containerId,
        bool force = false, CancellationToken cancellationToken = default)
    {
      var body = new { Container = containerId, Force = force };
      var result = await PostAsync(
          $"/networks/{Uri.EscapeDataString(networkId)}/disconnect", body, cancellationToken)
          .ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            ErrorCodes.Network.DisconnectFailed,
            CreateErrorContext($"POST /networks/{networkId}/disconnect", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Network>> InspectAsync(
        DriverContext context, string networkId,
        CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync($"/networks/{Uri.EscapeDataString(networkId)}", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Network>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Network.NotFound),
            CreateErrorContext($"GET /networks/{networkId}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Network>.Ok(ParseNetwork(result.Data));
    }

    /// <inheritdoc />
    public async Task<CommandResponse<NetworkPruneResult>> PruneAsync(
        DriverContext context, CancellationToken cancellationToken = default)
    {
      var result = await PostJsonElementAsync("/networks/prune", null, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<NetworkPruneResult>.Fail(result.ErrorMessage,
            result.StatusCode is 599 or 408
                ? MapHttpErrorCode(result.StatusCode)
                : ErrorCodes.Network.PruneFailed,
            CreateErrorContext("POST /networks/prune", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var pruneResult = new NetworkPruneResult();
      var deletedEl = result.Data.Prop("NetworksDeleted");
      if (deletedEl?.ValueKind == JsonValueKind.Array)
      {
        pruneResult.NetworksDeleted = [.. deletedEl.Value.EnumerateArray().Select(n => n.GetString())];
      }

      return CommandResponse<NetworkPruneResult>.Ok(pruneResult);
    }

    private static Network ParseNetwork(JsonElement token)
    {
      if (token.ValueKind != JsonValueKind.Object)
        return new Network();
      return new Network
      {
        Id = token.GetStringOrDefault("Id"),
        Name = token.GetStringOrDefault("Name"),
        Driver = token.GetStringOrDefault("Driver"),
        Scope = token.GetStringOrDefault("Scope"),
        Internal = token.GetBoolOrDefault("Internal"),
        IPv6 = token.GetBoolOrDefault("EnableIPv6"),
        Labels = token.GetStringDictionary("Labels"),
        Containers = ParseContainers(token)
      };
    }

    private static Dictionary<string, NetworkedContainer> ParseContainers(JsonElement token)
    {
      var containers = token.Prop("Containers", "containers");
      return containers?.ValueKind == JsonValueKind.Object
          ? containers.Value.Deserialize<Dictionary<string, NetworkedContainer>>() ?? []
          : [];
    }
  }
}
