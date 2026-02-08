using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Docker.Api.Components
{
    /// <summary>
    /// Docker API implementation of INetworkDriver.
    /// Uses /networks endpoints.
    /// </summary>
    public class DockerApiNetworkDriver : DockerApiDriverBase, INetworkDriver
    {
        public DockerApiNetworkDriver(IDockerApiConnection connection) : base(connection) { }

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

            if (!string.IsNullOrEmpty(config.Subnet) || !string.IsNullOrEmpty(config.Gateway))
            {
                var ipamConfig = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(config.Subnet)) ipamConfig["Subnet"] = config.Subnet;
                if (!string.IsNullOrEmpty(config.Gateway)) ipamConfig["Gateway"] = config.Gateway;

                body["IPAM"] = new Dictionary<string, object>
                {
                    ["Config"] = new[] { ipamConfig }
                };
            }

            var result = await PostJsonAsync<JObject>("/networks/create", body, cancellationToken);
            if (!result.Success)
                return CommandResponse<NetworkCreateResult>.Fail(result.ErrorMessage,
                    ErrorCodes.Network.CreateFailed,
                    CreateErrorContext("POST /networks/create", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var createResult = new NetworkCreateResult
            {
                Id = result.Data?.Value<string>("Id")
            };

            if (result.Data?["Warning"] != null)
                createResult.Warnings.Add(result.Data.Value<string>("Warning"));

            return CommandResponse<NetworkCreateResult>.Ok(createResult);
        }

        public async Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context, string networkId,
            CancellationToken cancellationToken = default)
        {
            var result = await DeleteAsync($"/networks/{networkId}", cancellationToken);
            if (!result.Success)
                return CommandResponse<Unit>.Fail(result.ErrorMessage,
                    MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Network.NotFound),
                    CreateErrorContext($"DELETE /networks/{networkId}", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            return CommandResponse<Unit>.Ok(Unit.Default);
        }

        public async Task<CommandResponse<IList<Network>>> ListAsync(
            DriverContext context, NetworkListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            var path = "/networks";
            if (filter?.Name != null)
            {
                var filters = $"{{\"name\":[\"{filter.Name}\"]}}";
                path += $"?filters={Uri.EscapeDataString(filters)}";
            }

            var result = await GetJsonAsync<JArray>(path, cancellationToken);
            if (!result.Success)
                return CommandResponse<IList<Network>>.Fail(result.ErrorMessage,
                    MapHttpErrorCode(result.StatusCode),
                    CreateErrorContext("GET /networks", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var networks = result.Data?.Select(ParseNetwork).ToList()
                ?? new List<Network>();
            return CommandResponse<IList<Network>>.Ok(networks);
        }

        public async Task<CommandResponse<Unit>> ConnectAsync(
            DriverContext context, string networkId, string containerId,
            CancellationToken cancellationToken = default)
        {
            var body = new { Container = containerId };
            var result = await PostAsync(
                $"/networks/{networkId}/connect", body, cancellationToken);
            if (!result.Success)
                return CommandResponse<Unit>.Fail(result.ErrorMessage,
                    ErrorCodes.Network.ConnectFailed,
                    CreateErrorContext($"POST /networks/{networkId}/connect", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            return CommandResponse<Unit>.Ok(Unit.Default);
        }

        public async Task<CommandResponse<Unit>> DisconnectAsync(
            DriverContext context, string networkId, string containerId,
            bool force = false, CancellationToken cancellationToken = default)
        {
            var body = new { Container = containerId, Force = force };
            var result = await PostAsync(
                $"/networks/{networkId}/disconnect", body, cancellationToken);
            if (!result.Success)
                return CommandResponse<Unit>.Fail(result.ErrorMessage,
                    ErrorCodes.Network.DisconnectFailed,
                    CreateErrorContext($"POST /networks/{networkId}/disconnect", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            return CommandResponse<Unit>.Ok(Unit.Default);
        }

        public async Task<CommandResponse<Network>> InspectAsync(
            DriverContext context, string networkId,
            CancellationToken cancellationToken = default)
        {
            var result = await GetJsonAsync<JObject>($"/networks/{networkId}", cancellationToken);
            if (!result.Success)
                return CommandResponse<Network>.Fail(result.ErrorMessage,
                    MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Network.NotFound),
                    CreateErrorContext($"GET /networks/{networkId}", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            return CommandResponse<Network>.Ok(ParseNetwork(result.Data));
        }

        public async Task<CommandResponse<NetworkPruneResult>> PruneAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var result = await PostJsonAsync<JObject>("/networks/prune", null, cancellationToken);
            if (!result.Success)
                return CommandResponse<NetworkPruneResult>.Fail(result.ErrorMessage,
                    ErrorCodes.Network.PruneFailed,
                    CreateErrorContext("POST /networks/prune", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var pruneResult = new NetworkPruneResult();
            if (result.Data?["NetworksDeleted"] is JArray deleted)
            {
                pruneResult.NetworksDeleted = deleted.Select(n => n.Value<string>()).ToList();
            }

            return CommandResponse<NetworkPruneResult>.Ok(pruneResult);
        }

        private static Network ParseNetwork(JToken token)
        {
            if (token == null) return new Network();
            return new Network
            {
                Id = token.Value<string>("Id"),
                Name = token.Value<string>("Name"),
                Driver = token.Value<string>("Driver"),
                Scope = token.Value<string>("Scope"),
                Internal = token.Value<bool?>("Internal") ?? false,
                IPv6 = token.Value<bool?>("EnableIPv6") ?? false,
                Labels = token["Labels"]?.ToObject<Dictionary<string, string>>()
                    ?? new Dictionary<string, string>()
            };
        }
    }
}
