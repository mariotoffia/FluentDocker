using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of ISystemDriver.
    /// </summary>
    public class DockerCliSystemDriver : DockerCliDriverBase, ISystemDriver
    {
        #region Information Operations

        /// <inheritdoc />
        public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("info --format \"{{json .}}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<SystemInfo>.Fail(
                        result.Error ?? "System info failed",
                        ErrorCodes.General.Unknown);
                }

                var info = JsonConvert.DeserializeObject<SystemInfo>(result.Output);
                return CommandResponse<SystemInfo>.Ok(info ?? new SystemInfo());
            }
            catch (Exception ex)
            {
                return CommandResponse<SystemInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("version --format \"{{json .}}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<VersionInfo>.Fail(
                        result.Error ?? "Version check failed",
                        ErrorCodes.General.Unknown);
                }

                var version = JsonConvert.DeserializeObject<VersionInfo>(result.Output);
                return CommandResponse<VersionInfo>.Ok(version ?? new VersionInfo());
            }
            catch (Exception ex)
            {
                return CommandResponse<VersionInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> PingAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("version", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail("Docker daemon not reachable", ErrorCodes.General.Unknown);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<bool>> IsWindowsEngineAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var versionResult = await GetVersionAsync(context, cancellationToken);
                if (!versionResult.Success)
                    return CommandResponse<bool>.Fail(versionResult.Error, versionResult.ErrorCode);

                var isWindows = versionResult.Data?.Os?.Equals("windows", StringComparison.OrdinalIgnoreCase) ?? false;
                return CommandResponse<bool>.Ok(isWindows);
            }
            catch (Exception ex)
            {
                return CommandResponse<bool>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<bool>> IsLinuxEngineAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var versionResult = await GetVersionAsync(context, cancellationToken);
                if (!versionResult.Success)
                    return CommandResponse<bool>.Ok(true); // Default to Linux

                var isLinux = !versionResult.Data?.Os?.Equals("windows", StringComparison.OrdinalIgnoreCase) ?? true;
                return CommandResponse<bool>.Ok(isLinux);
            }
            catch
            {
                return CommandResponse<bool>.Ok(true); // Default to Linux
            }
        }

        #endregion

        #region Maintenance Operations

        /// <inheritdoc />
        public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("system df --format \"{{json .}}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<DiskUsageInfo>.Fail(
                        result.Error ?? "Disk usage failed",
                        ErrorCodes.General.Unknown);
                }

                var info = new DiskUsageInfo();
                // Parse the JSON output - simplified for now
                return CommandResponse<DiskUsageInfo>.Ok(info);
            }
            catch (Exception ex)
            {
                return CommandResponse<DiskUsageInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
            DriverContext context,
            SystemPruneConfig config = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "system prune -f";
                if (config?.All == true)
                    args += " -a";
                if (config?.Volumes == true)
                    args += " --volumes";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<SystemPruneResult>.Fail(
                        result.Error ?? "System prune failed",
                        ErrorCodes.General.Unknown);
                }

                return CommandResponse<SystemPruneResult>.Ok(new SystemPruneResult());
            }
            catch (Exception ex)
            {
                return CommandResponse<SystemPruneResult>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        #endregion

        #region Daemon Operations (Docker Desktop specific)

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> SwitchDaemonAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteDockerCliCommandAsync("-SwitchDaemon", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Switch daemon failed", ErrorCodes.General.Unknown);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteDockerCliCommandAsync("-SwitchLinuxEngine", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Switch to Linux failed", ErrorCodes.General.Unknown);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteDockerCliCommandAsync("-SwitchWindowsEngine", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Switch to Windows failed", ErrorCodes.General.Unknown);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Executes a Docker CLI command (Docker Desktop specific).
        /// </summary>
        private async Task<SimpleCommandResult> ExecuteDockerCliCommandAsync(string arguments, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "dockercli".ResolveBinary(),
                            Arguments = arguments,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    var output = new StringBuilder();
                    var error = new StringBuilder();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            output.AppendLine(e.Data);
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            error.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    while (!process.WaitForExit(1000))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    return new SimpleCommandResult
                    {
                        Success = process.ExitCode == 0,
                        Output = output.ToString(),
                        Error = error.ToString(),
                        ExitCode = process.ExitCode
                    };
                }
                catch (Exception ex)
                {
                    return new SimpleCommandResult
                    {
                        Success = false,
                        Error = ex.Message,
                        ExitCode = -1
                    };
                }
            }, cancellationToken);
        }

        #endregion
    }
}

