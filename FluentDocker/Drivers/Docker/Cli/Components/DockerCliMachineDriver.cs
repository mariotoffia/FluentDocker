using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of IMachineDriver (for Docker Machine).
    /// </summary>
    public class DockerCliMachineDriver : DockerCliDriverBase, IMachineDriver
    {
        /// <summary>
        /// Creates a new instance with the specified binary resolver.
        /// </summary>
        public DockerCliMachineDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        /// <inheritdoc />
        public bool IsAvailable()
        {
            return BinaryResolver?.IsDockerMachineAvailable ?? false;
        }

        /// <inheritdoc />
        public async Task<CommandResponse<IList<MachineInfo>>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync("ls --format \"{{json .}}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<MachineInfo>>.Fail(
                        result.Error ?? "Machine list failed", ErrorCodes.Machine.ListFailed);
                }

                var machines = new List<MachineInfo>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var machine = JsonConvert.DeserializeObject<MachineInfo>(line);
                        if (machine != null) machines.Add(machine);
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }

                return CommandResponse<IList<MachineInfo>>.Ok(machines);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<MachineInfo>>.Fail(ex.Message, ErrorCodes.Machine.ListFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<MachineDetails>> InspectAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"inspect {machineName}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<MachineDetails>.Fail(
                        result.Error ?? "Machine inspect failed", ErrorCodes.Machine.InspectFailed);
                }

                var details = JsonConvert.DeserializeObject<MachineDetails>(result.Output);
                return details != null
                    ? CommandResponse<MachineDetails>.Ok(details)
                    : CommandResponse<MachineDetails>.Fail("Machine not found", ErrorCodes.Machine.NotFound);
            }
            catch (Exception ex)
            {
                return CommandResponse<MachineDetails>.Fail(ex.Message, ErrorCodes.Machine.InspectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StartAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"start {machineName}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine start failed", ErrorCodes.Machine.StartFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.StartFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StopAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"stop {machineName}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine stop failed", ErrorCodes.Machine.StopFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.StopFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RestartAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"restart {machineName}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine restart failed", ErrorCodes.Machine.RestartFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.RestartFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> CreateAsync(
            MachineCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"create -d {config.Driver}";
                if (config.Memory.HasValue)
                    args += $" --{config.Driver}-memory {config.Memory.Value}";
                if (config.DiskSize.HasValue)
                    args += $" --{config.Driver}-disk-size {config.DiskSize.Value}";
                if (config.CpuCount.HasValue)
                    args += $" --{config.Driver}-cpu-count {config.CpuCount.Value}";
                foreach (var opt in config.DriverOptions)
                    args += $" --{config.Driver}-{opt.Key} {opt.Value}";
                args += $" {config.Name}";

                var result = await ExecuteMachineCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine create failed", ErrorCodes.Machine.CreateFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.CreateFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> DeleteAsync(
            string machineName,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "rm -y";
                if (force)
                    args += " -f";
                args += $" {machineName}";

                var result = await ExecuteMachineCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine delete failed", ErrorCodes.Machine.DeleteFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.DeleteFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Dictionary<string, string>>> GetEnvAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"env {machineName}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Dictionary<string, string>>.Fail(
                        result.Error ?? "Machine env failed", ErrorCodes.Machine.EnvFailed);
                }

                var env = new Dictionary<string, string>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("export "))
                    {
                        var parts = line.Substring(7).Split('=');
                        if (parts.Length == 2)
                            env[parts[0]] = parts[1].Trim('"');
                    }
                }

                return CommandResponse<Dictionary<string, string>>.Ok(env);
            }
            catch (Exception ex)
            {
                return CommandResponse<Dictionary<string, string>>.Fail(ex.Message, ErrorCodes.Machine.EnvFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<string>> GetUrlAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"url {machineName}", cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output.Trim())
                    : CommandResponse<string>.Fail(result.Error ?? "Machine url failed", ErrorCodes.Machine.UrlFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Machine.UrlFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ServiceRunningState>> GetStatusAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"status {machineName}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ServiceRunningState>.Ok(ServiceRunningState.Unknown);
                }

                var status = result.Output.Trim();
                var state = status switch
                {
                    "Running" => ServiceRunningState.Running,
                    "Stopped" => ServiceRunningState.Stopped,
                    _ => ServiceRunningState.Unknown
                };

                return CommandResponse<ServiceRunningState>.Ok(state);
            }
            catch
            {
                return CommandResponse<ServiceRunningState>.Ok(ServiceRunningState.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RegenerateCertsAsync(
            string machineName,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "regenerate-certs";
                if (force)
                    args += " -f";
                args += $" {machineName}";

                var result = await ExecuteMachineCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Regenerate certs failed", ErrorCodes.Machine.RegenerateCertsFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.RegenerateCertsFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> UpgradeAsync(
            string machineName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"upgrade {machineName}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine upgrade failed", ErrorCodes.Machine.UpgradeFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.UpgradeFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<string>> SshAsync(
            string machineName,
            string command,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteMachineCommandAsync($"ssh {machineName} {command}", cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output)
                    : CommandResponse<string>.Fail(result.Error ?? "Machine ssh failed", ErrorCodes.Machine.SshFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Machine.SshFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> ScpAsync(
            string source,
            string destination,
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "scp";
                if (recursive)
                    args += " -r";
                args += $" {source} {destination}";

                var result = await ExecuteMachineCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Machine scp failed", ErrorCodes.Machine.ScpFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Machine.ScpFailed);
            }
        }
    }
}

