using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of INetworkDriver.
    /// </summary>
    public class DockerCliNetworkDriver : DockerCliDriverBase, INetworkDriver
    {
        /// <summary>
        /// Creates a new instance with the specified binary resolver.
        /// </summary>
        public DockerCliNetworkDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        /// <inheritdoc />
        public async Task<CommandResponse<NetworkCreateResult>> CreateAsync(
            DriverContext context,
            NetworkCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "network", "create" };

                if (!string.IsNullOrEmpty(config.Driver))
                    args.Add($"--driver {config.Driver}");

                if (!string.IsNullOrEmpty(config.Subnet))
                    args.Add($"--subnet {config.Subnet}");

                if (!string.IsNullOrEmpty(config.Gateway))
                    args.Add($"--gateway {config.Gateway}");

                if (config.EnableIPv6)
                    args.Add("--ipv6");

                if (config.Internal)
                    args.Add("--internal");

                if (config.Options != null)
                {
                    foreach (var opt in config.Options)
                        args.Add($"--opt {opt.Key}={opt.Value}");
                }

                if (config.Labels != null)
                {
                    foreach (var label in config.Labels)
                        args.Add($"--label {label.Key}={label.Value}");
                }

                args.Add(config.Name);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<NetworkCreateResult>.Fail(
                        result.Error ?? "Network creation failed",
                        ErrorCodes.Network.CreateFailed,
                        CreateErrorContext(context, "CreateNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<NetworkCreateResult>.Ok(
                    new NetworkCreateResult { Id = result.Output.Trim() });
            }
            catch (Exception ex)
            {
                return CommandResponse<NetworkCreateResult>.Fail(ex.Message, ErrorCodes.Network.CreateFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"network rm {networkId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Network removal failed",
                        ErrorCodes.Network.RemoveFailed,
                        CreateErrorContext(context, "RemoveNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.RemoveFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<IList<Network>>> ListAsync(
            DriverContext context,
            NetworkListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "network ls --format \"{{json .}}\"";

                if (filter != null)
                {
                    if (!string.IsNullOrEmpty(filter.Name))
                        args += $" --filter name={filter.Name}";

                    if (filter.Labels != null)
                    {
                        foreach (var label in filter.Labels)
                            args += $" --filter label={label.Key}={label.Value}";
                    }
                }

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<Network>>.Fail(
                        result.Error ?? "Network list failed",
                        ErrorCodes.General.Unknown,
                        CreateErrorContext(context, "ListNetworks", result),
                        result.ExitCode);
                }

                var networks = new List<Network>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        var network = JsonConvert.DeserializeObject<Network>(line);
                        if (network != null)
                            networks.Add(network);
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }

                return CommandResponse<IList<Network>>.Ok(networks);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<Network>>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Network>> InspectAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"network inspect {networkId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Network>.Fail(
                        result.Error ?? "Network inspect failed",
                        ErrorCodes.Network.InspectFailed,
                        CreateErrorContext(context, "InspectNetwork", result),
                        result.ExitCode);
                }

                var networks = JsonConvert.DeserializeObject<List<Network>>(result.Output);
                return CommandResponse<Network>.Ok(networks?.FirstOrDefault() ?? new Network());
            }
            catch (Exception ex)
            {
                return CommandResponse<Network>.Fail(ex.Message, ErrorCodes.Network.InspectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> ConnectAsync(
            DriverContext context,
            string networkId,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"network connect {networkId} {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Network connect failed",
                        ErrorCodes.Network.ConnectFailed,
                        CreateErrorContext(context, "ConnectNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.ConnectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> DisconnectAsync(
            DriverContext context,
            string networkId,
            string containerId,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "network disconnect";
                if (force)
                    args += " --force";
                args += $" {networkId} {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Network disconnect failed",
                        ErrorCodes.Network.DisconnectFailed,
                        CreateErrorContext(context, "DisconnectNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.DisconnectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<NetworkPruneResult>> PruneAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("network prune --force", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<NetworkPruneResult>.Fail(
                        result.Error ?? "Network prune failed",
                        ErrorCodes.Network.PruneFailed);
                }

                return CommandResponse<NetworkPruneResult>.Ok(new NetworkPruneResult());
            }
            catch (Exception ex)
            {
                return CommandResponse<NetworkPruneResult>.Fail(ex.Message, ErrorCodes.Network.PruneFailed);
            }
        }
    }
}

