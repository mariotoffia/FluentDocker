using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Podman CLI implementation of machine (VM) management operations.
    /// </summary>
    public class PodmanCliMachineDriver : PodmanCliDriverBase, IPodmanMachineDriver
    {
        public PodmanCliMachineDriver(IPodmanBinaryResolver binaryResolver)
            : base(binaryResolver)
        {
        }

        #region Lifecycle

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> InitAsync(
            DriverContext context, MachineInitConfig config,
            CancellationToken cancellationToken = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var args = BuildInitArgs(config);
            return await ExecuteSimpleMachineCommandAsync(
                args, ErrorCodes.Machine.InitFailed, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StartAsync(
            DriverContext context, string name = null,
            CancellationToken cancellationToken = default)
        {
            var args = string.IsNullOrEmpty(name) ? "machine start" : $"machine start {name}";
            return await ExecuteSimpleMachineCommandAsync(
                args, ErrorCodes.Machine.StartFailed, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StopAsync(
            DriverContext context, string name = null,
            CancellationToken cancellationToken = default)
        {
            var args = string.IsNullOrEmpty(name) ? "machine stop" : $"machine stop {name}";
            return await ExecuteSimpleMachineCommandAsync(
                args, ErrorCodes.Machine.StopFailed, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context, string name = null, bool force = false,
            CancellationToken cancellationToken = default)
        {
            var args = "machine rm";
            if (force) args += " -f";
            if (!string.IsNullOrEmpty(name)) args += $" {name}";

            return await ExecuteSimpleMachineCommandAsync(
                args, ErrorCodes.Machine.RemoveFailed, cancellationToken);
        }

        #endregion

        #region Query

        /// <inheritdoc />
        public async Task<CommandResponse<IList<MachineInfo>>> ListAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    "machine list --format json", cancellationToken);

                if (!result.Success)
                    return CommandResponse<IList<MachineInfo>>.Fail(
                        result.Error ?? "Machine list failed",
                        ErrorCodes.Machine.ListFailed, result.ExitCode);

                var machines = ParseMachineList(result.Output);
                return CommandResponse<IList<MachineInfo>>.Ok(machines);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<MachineInfo>>.Fail(
                    ex.Message, ErrorCodes.Machine.ListFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<MachineInspectResult>> InspectAsync(
            DriverContext context, string name = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = string.IsNullOrEmpty(name)
                    ? "machine inspect"
                    : $"machine inspect {name}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                    return CommandResponse<MachineInspectResult>.Fail(
                        result.Error ?? "Machine inspect failed",
                        ErrorCodes.Machine.InspectFailed, result.ExitCode);

                var inspect = ParseMachineInspect(result.Output);
                return CommandResponse<MachineInspectResult>.Ok(inspect);
            }
            catch (Exception ex)
            {
                return CommandResponse<MachineInspectResult>.Fail(
                    ex.Message, ErrorCodes.Machine.InspectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<MachineHostInfo>> InfoAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    "machine info --format json", cancellationToken);

                if (!result.Success)
                    return CommandResponse<MachineHostInfo>.Fail(
                        result.Error ?? "Machine info failed",
                        ErrorCodes.Machine.InfoFailed, result.ExitCode);

                var info = ParseMachineInfo(result.Output);
                return CommandResponse<MachineHostInfo>.Ok(info);
            }
            catch (Exception ex)
            {
                return CommandResponse<MachineHostInfo>.Fail(
                    ex.Message, ErrorCodes.Machine.InfoFailed);
            }
        }

        #endregion

        #region Interaction

        /// <inheritdoc />
        public async Task<CommandResponse<string>> SshAsync(
            DriverContext context, string name = null, string command = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "machine ssh";
                if (!string.IsNullOrEmpty(name)) args += $" {name}";
                if (!string.IsNullOrEmpty(command)) args += $" {command}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                    return CommandResponse<string>.Fail(
                        result.Error ?? "Machine SSH failed",
                        ErrorCodes.Machine.SshFailed, result.ExitCode);

                return CommandResponse<string>.Ok(result.Output?.TrimEnd());
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(
                    ex.Message, ErrorCodes.Machine.SshFailed);
            }
        }

        #endregion

        #region Configuration

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> SetAsync(
            DriverContext context, MachineSetConfig config, string name = null,
            CancellationToken cancellationToken = default)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var args = BuildSetArgs(config, name);
            return await ExecuteSimpleMachineCommandAsync(
                args, ErrorCodes.Machine.SetFailed, cancellationToken);
        }

        #endregion

        #region Argument Building

        internal static string BuildInitArgs(MachineInitConfig config)
        {
            var args = "machine init";

            if (config.Cpus.HasValue)
                args += $" --cpus {config.Cpus.Value}";
            if (config.DiskSizeGiB.HasValue)
                args += $" --disk-size {config.DiskSizeGiB.Value}";
            if (config.MemoryMiB.HasValue)
                args += $" --memory {config.MemoryMiB.Value}";
            if (config.Rootful)
                args += " --rootful";
            if (!string.IsNullOrEmpty(config.Image))
                args += $" --image {config.Image}";
            if (!string.IsNullOrEmpty(config.Username))
                args += $" --username {config.Username}";
            if (config.Now)
                args += " --now";

            foreach (var vol in config.Volumes)
                args += $" -v {vol}";

            if (!string.IsNullOrEmpty(config.Name))
                args += $" {config.Name}";

            return args;
        }

        internal static string BuildSetArgs(MachineSetConfig config, string name = null)
        {
            var args = "machine set";

            if (config.Cpus.HasValue)
                args += $" --cpus {config.Cpus.Value}";
            if (config.DiskSizeGiB.HasValue)
                args += $" --disk-size {config.DiskSizeGiB.Value}";
            if (config.MemoryMiB.HasValue)
                args += $" --memory {config.MemoryMiB.Value}";
            if (config.Rootful.HasValue)
                args += config.Rootful.Value ? " --rootful" : " --rootful=false";

            if (!string.IsNullOrEmpty(name))
                args += $" {name}";

            return args;
        }

        #endregion

        #region JSON Parsing

        internal static IList<MachineInfo> ParseMachineList(string json)
        {
            var machines = new List<MachineInfo>();
            if (string.IsNullOrWhiteSpace(json)) return machines;

            try
            {
                var trimmed = json.Trim();
                if (trimmed.StartsWith("["))
                {
                    var arr = JArray.Parse(trimmed);
                    machines.AddRange(arr.Select(ParseMachineInfoFromToken));
                }
                else
                {
                    foreach (var line in trimmed.Split('\n',
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        var obj = JObject.Parse(line.Trim());
                        machines.Add(ParseMachineInfoFromToken(obj));
                    }
                }
            }
            catch { /* Return partial results */ }

            return machines;
        }

        private static MachineInfo ParseMachineInfoFromToken(JToken token)
        {
            var info = new MachineInfo
            {
                Name = (token["Name"] ?? token["name"])?.Value<string>(),
                VMType = (token["VMType"] ?? token["vmType"] ?? token["vmtype"])?.Value<string>(),
                Created = (token["Created"] ?? token["created"])?.Value<string>(),
                LastUp = (token["LastUp"] ?? token["lastUp"] ?? token["lastup"])?.Value<string>()
            };

            var def = token["Default"] ?? token["default"];
            if (def != null) info.Default = def.Value<bool>();

            var running = token["Running"] ?? token["running"];
            if (running != null) info.Running = running.Value<bool>();

            var cpus = token["CPUs"] ?? token["cpus"] ?? token["Cpus"];
            if (cpus != null) info.Cpus = cpus.Value<int>();

            var mem = token["Memory"] ?? token["memory"];
            if (mem != null)
                info.Memory = ParseLongValue(mem);

            var disk = token["DiskSize"] ?? token["diskSize"] ?? token["disk_size"];
            if (disk != null)
                info.DiskSize = ParseLongValue(disk);

            return info;
        }

        internal static MachineInspectResult ParseMachineInspect(string json)
        {
            var result = new MachineInspectResult();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var trimmed = json.Trim();
                // inspect can return a JSON array with one element
                JObject obj;
                if (trimmed.StartsWith("["))
                {
                    var arr = JArray.Parse(trimmed);
                    obj = arr.FirstOrDefault() as JObject;
                    if (obj == null) return result;
                }
                else
                {
                    obj = JObject.Parse(trimmed);
                }

                result.Name = (obj["Name"] ?? obj["name"])?.Value<string>();
                result.Created = (obj["Created"] ?? obj["created"])?.Value<string>();
                result.LastUp = (obj["LastUp"] ?? obj["lastUp"])?.Value<string>();

                var state = obj["State"] ?? obj["state"];
                if (state != null)
                    result.State = state.Type == JTokenType.String
                        ? state.Value<string>()
                        : state.ToString();

                var rootful = obj["Rootful"] ?? obj["rootful"];
                if (rootful != null) result.Rootful = rootful.Value<bool>();

                var configDir = obj["ConfigDir"] ?? obj["configDir"];
                if (configDir is JObject cd)
                    result.ConfigDir = (cd["Path"] ?? cd["path"])?.Value<string>();
                else if (configDir != null)
                    result.ConfigDir = configDir.Value<string>();

                var resources = obj["Resources"] ?? obj["resources"];
                if (resources is JObject res)
                {
                    result.Resources = new MachineResources
                    {
                        Cpus = (res["CPUs"] ?? res["cpus"] ?? res["Cpus"])?.Value<int>() ?? 0,
                        MemoryMiB = (res["Memory"] ?? res["memory"])?.Value<int>() ?? 0,
                        DiskSizeGiB = (res["DiskSize"] ?? res["diskSize"])?.Value<int>() ?? 0
                    };
                }

                var connInfo = obj["ConnectionInfo"] ?? obj["connectionInfo"];
                if (connInfo is JObject ci)
                {
                    var socket = ci["PodmanSocket"] ?? ci["podmanSocket"];
                    if (socket is JObject sock)
                    {
                        result.ConnectionInfo = new MachineConnectionInfo
                        {
                            PodmanSocketPath = (sock["Path"] ?? sock["path"])?.Value<string>()
                        };
                    }
                }
            }
            catch { /* Return partial results */ }

            return result;
        }

        internal static MachineHostInfo ParseMachineInfo(string json)
        {
            var result = new MachineHostInfo();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var obj = JObject.Parse(json.Trim());

                var host = obj["Host"] ?? obj["host"];
                if (host is JObject h)
                {
                    result.Arch = (h["Arch"] ?? h["arch"])?.Value<string>();
                    result.OS = (h["CurrentMachine"] ?? h["OS"] ?? h["os"])?.Value<string>();
                    result.VMType = (h["VMType"] ?? h["vmType"])?.Value<string>();
                    var num = h["NumberOfMachines"] ?? h["numberOfMachines"];
                    if (num != null) result.NumberOfMachines = num.Value<int>();
                    result.MachineConfigDir =
                        (h["MachineConfigDir"] ?? h["machineConfigDir"])?.Value<string>();
                }

                var version = obj["Version"] ?? obj["version"];
                if (version is JObject v)
                {
                    result.ApiVersion =
                        (v["APIVersion"] ?? v["apiVersion"] ?? v["ApiVersion"])?.Value<string>();
                    result.Version =
                        (v["Version"] ?? v["version"])?.Value<string>();
                }
            }
            catch { /* Return partial results */ }

            return result;
        }

        private static long ParseLongValue(JToken token)
        {
            if (token.Type == JTokenType.String)
                return long.TryParse(token.Value<string>(), out var v) ? v : 0;
            return token.Value<long>();
        }

        #endregion

        #region Helpers

        private async Task<CommandResponse<Unit>> ExecuteSimpleMachineCommandAsync(
            string args, string errorCode, CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? $"Machine command failed: {args}",
                        errorCode, result.ExitCode);
            }
            catch (Exception ex) when (ex is not ArgumentNullException)
            {
                return CommandResponse<Unit>.Fail(ex.Message, errorCode);
            }
        }

        #endregion
    }
}
