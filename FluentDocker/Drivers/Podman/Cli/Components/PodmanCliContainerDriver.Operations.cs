using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Podman CLI container driver - execution, copy, and monitoring operations.
    /// </summary>
    public partial class PodmanCliContainerDriver
    {
        #region Information Operations (continued)

        /// <inheritdoc />
        public async Task<CommandResponse<string>> GetLogsAsync(
            DriverContext context, string containerId,
            bool follow = false, int? tail = null, bool timestamps = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "logs";
                if (follow) args += " --follow";
                if (tail.HasValue) args += $" --tail {tail.Value}";
                if (timestamps) args += " --timestamps";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<string>.Fail(
                        result.Error ?? "Get logs failed", ErrorCodes.General.Unknown);

                return CommandResponse<string>.Ok(result.Output);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerProcesses>> TopAsync(
            DriverContext context, string containerId, string psOptions = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"top {containerId}";
                if (!string.IsNullOrEmpty(psOptions))
                    args += $" {psOptions}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<ContainerProcesses>.Fail(
                        result.Error ?? "Container top failed", ErrorCodes.Container.TopFailed);

                var processes = ParseTopOutput(result.Output);
                return CommandResponse<ContainerProcesses>.Ok(processes);
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerProcesses>.Fail(
                    ex.Message, ErrorCodes.Container.TopFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"diff {containerId}", cancellationToken);
                if (!result.Success)
                    return CommandResponse<IList<FilesystemChange>>.Fail(
                        result.Error ?? "Container diff failed", ErrorCodes.Container.DiffFailed);

                var changes = ParseDiffOutput(result.Output);
                return CommandResponse<IList<FilesystemChange>>.Ok(changes);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<FilesystemChange>>.Fail(
                    ex.Message, ErrorCodes.Container.DiffFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerStatsResult>> StatsAsync(
            DriverContext context, string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"stats --no-stream --format json {containerId}", cancellationToken);
                if (!result.Success)
                    return CommandResponse<ContainerStatsResult>.Fail(
                        result.Error ?? "Container stats failed", ErrorCodes.Container.StatsFailed);

                var stats = ParseStatsOutput(result.Output);
                return CommandResponse<ContainerStatsResult>.Ok(stats);
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerStatsResult>.Fail(
                    ex.Message, ErrorCodes.Container.StatsFailed);
            }
        }

        #endregion

        #region Execution Operations

        /// <inheritdoc />
        public async Task<CommandResponse<ExecResult>> ExecAsync(
            DriverContext context, string containerId, ExecConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "exec";
                if (config.Detach) args += " -d";
                if (config.Tty) args += " -t";
                if (config.Interactive) args += " -i";
                if (config.Privileged) args += " --privileged";
                if (!string.IsNullOrEmpty(config.User))
                    args += $" --user {config.User}";
                if (!string.IsNullOrEmpty(config.WorkingDir))
                    args += $" -w {config.WorkingDir}";

                if (config.Environment != null)
                    foreach (var env in config.Environment)
                        args += $" -e {env.Key}={env.Value}";

                args += $" {containerId}";

                if (config.Command != null)
                    foreach (var cmd in config.Command)
                        args += $" {QuoteArgumentIfNeeded(cmd)}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return CommandResponse<ExecResult>.Ok(new ExecResult
                {
                    ExitCode = result.ExitCode,
                    StdOut = result.Output,
                    StdErr = result.Error
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ExecResult>.Fail(
                    ex.Message, ErrorCodes.Container.ExecFailed);
            }
        }

        #endregion

        #region Copy Operations

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> CopyToAsync(
            DriverContext context, string containerId,
            string hostPath, string containerPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"cp {hostPath} {containerId}:{containerPath}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Copy to container failed", ErrorCodes.Container.CopyFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> CopyFromAsync(
            DriverContext context, string containerId,
            string containerPath, string hostPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"cp {containerId}:{containerPath} {hostPath}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Copy from container failed", ErrorCodes.Container.CopyFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
            }
        }

        #endregion

        #region Export/Update Operations

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> ExportAsync(
            DriverContext context, string containerId, string outputPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"export -o {outputPath} {containerId}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container export failed", ErrorCodes.Container.ExportFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.ExportFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RenameAsync(
            DriverContext context, string containerId, string newName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync(
                    $"rename {containerId} {newName}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container rename failed", ErrorCodes.Container.RenameFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RenameFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> UpdateAsync(
            DriverContext context, string containerId, ContainerUpdateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"update";
                if (config.MemoryLimit.HasValue)
                    args += $" --memory {config.MemoryLimit.Value}";
                if (config.MemoryReservation.HasValue)
                    args += $" --memory-reservation {config.MemoryReservation.Value}";
                if (config.CpuShares.HasValue)
                    args += $" --cpu-shares {config.CpuShares.Value}";
                if (config.CpuPeriod.HasValue)
                    args += $" --cpu-period {config.CpuPeriod.Value}";
                if (config.CpuQuota.HasValue)
                    args += $" --cpu-quota {config.CpuQuota.Value}";
                if (!string.IsNullOrEmpty(config.CpusetCpus))
                    args += $" --cpuset-cpus {config.CpusetCpus}";
                if (!string.IsNullOrEmpty(config.RestartPolicy))
                    args += $" --restart {config.RestartPolicy}";
                if (config.PidsLimit.HasValue)
                    args += $" --pids-limit {config.PidsLimit.Value}";

                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Container update failed", ErrorCodes.Container.UpdateFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UpdateFailed);
            }
        }

        #endregion

        #region Output Parsing

        private static ContainerProcesses ParseTopOutput(string output)
        {
            var processes = new ContainerProcesses();
            if (string.IsNullOrWhiteSpace(output)) return processes;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return processes;

            // First line is header
            processes.Titles = new List<string>(lines[0].Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

            for (var i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                processes.Processes.Add(new List<string>(fields));
            }

            return processes;
        }

        private static IList<FilesystemChange> ParseDiffOutput(string output)
        {
            var changes = new List<FilesystemChange>();
            if (string.IsNullOrWhiteSpace(output)) return changes;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 2) continue;

                changes.Add(new FilesystemChange
                {
                    Kind = trimmed[0].ToString(),
                    Path = trimmed.Substring(2).Trim()
                });
            }

            return changes;
        }

        private static ContainerStatsResult ParseStatsOutput(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ContainerStatsResult();

            try
            {
                var trimmed = json.Trim();
                JToken token;

                if (trimmed.StartsWith("["))
                    token = JArray.Parse(trimmed).First;
                else
                    token = JObject.Parse(trimmed);

                return new ContainerStatsResult
                {
                    ContainerId = token["ContainerID"]?.Value<string>()
                                  ?? token["container_id"]?.Value<string>(),
                    Name = token["Name"]?.Value<string>() ?? token["name"]?.Value<string>()
                };
            }
            catch
            {
                return new ContainerStatsResult();
            }
        }

        private static string QuoteArgumentIfNeeded(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return arg;
            return arg.Contains(' ') ? $"\"{arg}\"" : arg;
        }

        #endregion
    }
}
