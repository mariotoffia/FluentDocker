using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Podman CLI implementation of IContainerDriver.
    /// Core lifecycle and information operations.
    /// </summary>
    public partial class PodmanCliContainerDriver : PodmanCliDriverBase, IContainerDriver
    {
        public PodmanCliContainerDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        #region Lifecycle Operations

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
            DriverContext context, ContainerCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildCreateArgs("create", config);
                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<ContainerCreateResult>.Fail(
                        result.Error ?? "Container create failed", ErrorCodes.Container.CreateFailed);

                return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult
                {
                    Id = result.Output?.Trim(),
                    Name = config.Name
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerCreateResult>.Fail(
                    ex.Message, ErrorCodes.Container.CreateFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerRunResult>> RunAsync(
            DriverContext context, ContainerCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildCreateArgs("run", config);
                if (config.Detach) args = args.Insert(3, " -d");

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<ContainerRunResult>.Fail(
                        result.Error ?? "Container run failed", ErrorCodes.Container.CreateFailed);

                return CommandResponse<ContainerRunResult>.Ok(new ContainerRunResult
                {
                    Id = config.Detach ? result.Output?.Trim() : null,
                    Output = config.Detach ? null : result.Output
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerRunResult>.Fail(
                    ex.Message, ErrorCodes.Container.CreateFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StartAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"start {containerId}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container start failed", ErrorCodes.Container.StartFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StartFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StopAsync(
            DriverContext context, string containerId, int? timeout = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "stop";
                if (timeout.HasValue) args += $" -t {timeout.Value}";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container stop failed", ErrorCodes.Container.StopFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StopFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RestartAsync(
            DriverContext context, string containerId, int? timeout = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "restart";
                if (timeout.HasValue) args += $" -t {timeout.Value}";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container restart failed", ErrorCodes.Container.RestartFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RestartFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> PauseAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"pause {containerId}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container pause failed", ErrorCodes.Container.PauseFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.PauseFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> UnpauseAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"unpause {containerId}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container unpause failed", ErrorCodes.Container.UnpauseFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UnpauseFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> KillAsync(
            DriverContext context, string containerId, string signal = "SIGKILL",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"kill --signal {signal} {containerId}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container kill failed", ErrorCodes.Container.KillFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.KillFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context, string containerId,
            bool force = false, bool removeVolumes = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "rm";
                if (force) args += " -f";
                if (removeVolumes) args += " -v";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container remove failed", ErrorCodes.Container.RemoveFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RemoveFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerWaitResult>> WaitAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"wait {containerId}", cancellationToken);
                if (!result.Success)
                    return CommandResponse<ContainerWaitResult>.Fail(
                        result.Error ?? "Container wait failed", ErrorCodes.Container.WaitFailed);

                int.TryParse(result.Output?.Trim(), out var exitCode);
                return CommandResponse<ContainerWaitResult>.Ok(new ContainerWaitResult
                {
                    ExitCode = exitCode
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerWaitResult>.Fail(
                    ex.Message, ErrorCodes.Container.WaitFailed);
            }
        }

        #endregion

        #region Information Operations

        /// <inheritdoc />
        public async Task<CommandResponse<Container>> InspectAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"inspect {containerId}", cancellationToken);
                if (!result.Success)
                    return CommandResponse<Container>.Fail(
                        result.Error ?? "Container inspect failed",
                        ErrorCodes.Container.InspectFailed);

                var container = ParseContainerInspect(result.Output);
                return CommandResponse<Container>.Ok(container);
            }
            catch (Exception ex)
            {
                return CommandResponse<Container>.Fail(
                    ex.Message, ErrorCodes.Container.InspectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<IList<Container>>> ListAsync(
            DriverContext context, ContainerListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "ps --format json";
                if (filter?.All == true) args += " -a";
                if (!string.IsNullOrEmpty(filter?.Name))
                    args += $" --filter name={filter.Name}";
                if (!string.IsNullOrEmpty(filter?.Status))
                    args += $" --filter status={filter.Status}";
                if (!string.IsNullOrEmpty(filter?.Id))
                    args += $" --filter id={filter.Id}";
                if (filter?.Limit.HasValue == true)
                    args += $" --last {filter.Limit.Value}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<IList<Container>>.Fail(
                        result.Error ?? "Container list failed", ErrorCodes.General.Unknown);

                var containers = ParseContainerList(result.Output);
                return CommandResponse<IList<Container>>.Ok(containers);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<Container>>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        #endregion

        #region Argument Building

        private static string BuildCreateArgs(string command, ContainerCreateConfig config)
        {
            var args = command;

            if (!string.IsNullOrEmpty(config.Name))
                args += $" --name {config.Name}";
            if (!string.IsNullOrEmpty(config.Hostname))
                args += $" --hostname {config.Hostname}";
            if (!string.IsNullOrEmpty(config.User))
                args += $" --user {config.User}";
            if (!string.IsNullOrEmpty(config.WorkingDirectory))
                args += $" -w {config.WorkingDirectory}";
            if (!string.IsNullOrEmpty(config.NetworkMode))
                args += $" --network {config.NetworkMode}";
            if (!string.IsNullOrEmpty(config.RestartPolicy))
                args += $" --restart {config.RestartPolicy}";
            if (!string.IsNullOrEmpty(config.StopSignal))
                args += $" --stop-signal {config.StopSignal}";
            if (config.StopTimeout.HasValue)
                args += $" --stop-timeout {config.StopTimeout.Value}";
            if (config.Privileged) args += " --privileged";
            if (config.AutoRemove) args += " --rm";
            if (config.Tty) args += " -t";
            if (config.Interactive) args += " -i";
            if (config.MemoryLimit.HasValue)
                args += $" --memory {config.MemoryLimit.Value}";
            if (config.CpuShares.HasValue)
                args += $" --cpu-shares {config.CpuShares.Value}";
            if (!string.IsNullOrEmpty(config.Ipv4Address))
                args += $" --ip {config.Ipv4Address}";
            if (!string.IsNullOrEmpty(config.Ipv6Address))
                args += $" --ip6 {config.Ipv6Address}";
            if (!string.IsNullOrEmpty(config.Pod))
                args += $" --pod {config.Pod}";

            foreach (var env in config.Environment)
                args += $" -e {env.Key}={env.Value}";
            foreach (var port in config.PortBindings)
                args += $" -p {port.Value}:{port.Key}";
            foreach (var vol in config.Volumes)
                args += $" -v {vol.Key}:{vol.Value}";
            foreach (var label in config.Labels)
                args += $" --label {label.Key}={label.Value}";
            foreach (var network in config.Networks)
                args += $" --network {network}";
            foreach (var dns in config.Dns)
                args += $" --dns {dns}";
            foreach (var host in config.ExtraHosts)
                args += $" --add-host {host.Key}:{host.Value}";
            foreach (var link in config.Links)
                args += $" --link {link}";

            if (config.Entrypoint != null && config.Entrypoint.Length > 0)
                args += $" --entrypoint {string.Join(" ", config.Entrypoint)}";

            if (config.HealthCheck != null)
            {
                if (config.HealthCheck.Test != null && config.HealthCheck.Test.Length > 0)
                    args += $" --health-cmd \"{string.Join(" ", config.HealthCheck.Test)}\"";
                if (!string.IsNullOrEmpty(config.HealthCheck.Interval))
                    args += $" --health-interval {config.HealthCheck.Interval}";
                if (!string.IsNullOrEmpty(config.HealthCheck.Timeout))
                    args += $" --health-timeout {config.HealthCheck.Timeout}";
                if (config.HealthCheck.Retries > 0)
                    args += $" --health-retries {config.HealthCheck.Retries}";
                if (!string.IsNullOrEmpty(config.HealthCheck.StartPeriod))
                    args += $" --health-start-period {config.HealthCheck.StartPeriod}";
            }

            args += $" {config.Image}";

            if (config.Command != null && config.Command.Length > 0)
                args += $" {string.Join(" ", config.Command)}";

            return args;
        }

        #endregion

        #region JSON Parsing

        private static IList<Container> ParseContainerList(string json)
        {
            var containers = new List<Container>();
            if (string.IsNullOrWhiteSpace(json)) return containers;

            try
            {
                var trimmed = json.Trim();
                if (trimmed.StartsWith("["))
                {
                    var arr = JArray.Parse(trimmed);
                    foreach (var token in arr)
                        containers.Add(ParseContainerFromListToken(token));
                }
                else
                {
                    foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        containers.Add(ParseContainerFromListToken(JObject.Parse(line.Trim())));
                }
            }
            catch { /* Return partial results */ }

            return containers;
        }

        private static Container ParseContainerFromListToken(JToken token)
        {
            // Names can be a JSON array ["name"] or a plain string
            var names = token["Names"] ?? token["Name"];
            string name = null;
            if (names is JArray namesArr && namesArr.Count > 0)
                name = namesArr[0].Value<string>();
            else if (names != null)
                name = names.Value<string>();

            return new Container
            {
                Id = token["Id"]?.Value<string>() ?? token["ID"]?.Value<string>(),
                Image = token["Image"]?.Value<string>(),
                Name = name,
                State = new ContainerState
                {
                    Status = token["State"]?.Value<string>() ?? token["Status"]?.Value<string>()
                }
            };
        }

        private static Container ParseContainerInspect(string json)
        {
            try
            {
                var trimmed = json.Trim();
                JToken token;
                if (trimmed.StartsWith("["))
                    token = JArray.Parse(trimmed).First;
                else
                    token = JObject.Parse(trimmed);

                return new Container
                {
                    Id = token["Id"]?.Value<string>(),
                    Image = token["Image"]?.Value<string>(),
                    Name = token["Name"]?.Value<string>(),
                    State = ParseContainerState(token["State"]),
                    Driver = token["Driver"]?.Value<string>()
                };
            }
            catch
            {
                return new Container();
            }
        }

        private static ContainerState ParseContainerState(JToken stateToken)
        {
            if (stateToken == null) return new ContainerState();

            return new ContainerState
            {
                Status = stateToken["Status"]?.Value<string>(),
                Running = stateToken["Running"]?.Value<bool>() ?? false,
                Paused = stateToken["Paused"]?.Value<bool>() ?? false,
                Dead = stateToken["Dead"]?.Value<bool>() ?? false,
                ExitCode = stateToken["ExitCode"]?.Value<int>() ?? 0
            };
        }

        #endregion
    }
}
