using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Podman CLI implementation of ISystemDriver.
    /// Adapted for Podman's daemonless architecture.
    /// </summary>
    public class PodmanCliSystemDriver : PodmanCliDriverBase, ISystemDriver
    {
        public PodmanCliSystemDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        #region Information Operations

        /// <inheritdoc />
        public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("info --format json", cancellationToken);
                if (!result.Success)
                    return CommandResponse<SystemInfo>.Fail(
                        result.Error ?? "System info failed", ErrorCodes.General.Unknown);

                var info = ParseSystemInfo(result.Output);
                info.PopulateMeta();
                return CommandResponse<SystemInfo>.Ok(info);
            }
            catch (Exception ex)
            {
                return CommandResponse<SystemInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("version --format json", cancellationToken);
                if (!result.Success)
                    return CommandResponse<VersionInfo>.Fail(
                        result.Error ?? "Version check failed", ErrorCodes.General.Unknown);

                var version = ParseVersionInfo(result.Output);
                version.PopulateMeta();
                return CommandResponse<VersionInfo>.Ok(version);
            }
            catch (Exception ex)
            {
                return CommandResponse<VersionInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> PingAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Podman is daemonless; verify it works by running 'podman info'
                var result = await ExecuteCommandAsync("info", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail("Podman is not reachable", ErrorCodes.General.Unknown);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public Task<CommandResponse<bool>> IsWindowsEngineAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            // Podman always uses Linux containers (even on macOS/Windows via VM)
            return Task.FromResult(CommandResponse<bool>.Ok(false));
        }

        /// <inheritdoc />
        public Task<CommandResponse<bool>> IsLinuxEngineAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            // Podman always uses Linux containers
            return Task.FromResult(CommandResponse<bool>.Ok(true));
        }

        #endregion

        #region Maintenance Operations

        /// <inheritdoc />
        public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("system df --format json", cancellationToken);
                if (!result.Success)
                    return CommandResponse<DiskUsageInfo>.Fail(
                        result.Error ?? "Disk usage failed", ErrorCodes.General.Unknown);

                var info = new DiskUsageInfo();
                // Podman system df JSON output is line-separated objects; simplified parsing
                return CommandResponse<DiskUsageInfo>.Ok(info);
            }
            catch (Exception ex)
            {
                return CommandResponse<DiskUsageInfo>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
            DriverContext context, SystemPruneConfig config = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "system prune -f";
                if (config?.All == true) args += " -a";
                if (config?.Volumes == true) args += " --volumes";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<SystemPruneResult>.Fail(
                        result.Error ?? "System prune failed", ErrorCodes.General.Unknown);

                return CommandResponse<SystemPruneResult>.Ok(new SystemPruneResult());
            }
            catch (Exception ex)
            {
                return CommandResponse<SystemPruneResult>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        #endregion

        #region Daemon Operations (Not applicable to Podman)

        /// <inheritdoc />
        public Task<CommandResponse<Unit>> SwitchDaemonAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            // Podman is daemonless - no-op
            return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
        }

        /// <inheritdoc />
        public Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            // Podman always runs Linux containers - no-op
            return Task.FromResult(CommandResponse<Unit>.Ok(Unit.Default));
        }

        /// <inheritdoc />
        public Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            // Podman does not support Windows containers
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Podman does not support Windows containers",
                ErrorCodes.Driver.CapabilityNotSupported));
        }

        #endregion

        #region JSON Parsing

        private static SystemInfo ParseSystemInfo(string json)
        {
            var info = new SystemInfo();
            try
            {
                var obj = JObject.Parse(json);

                // Podman info structure: { host: {...}, store: {...}, ... }
                var host = obj["host"];
                if (host != null)
                {
                    info.OperatingSystem = host["os"]?.Value<string>();
                    info.Architecture = host["arch"]?.Value<string>();
                    info.Hostname = host["hostname"]?.Value<string>();
                    info.KernelVersion = host["kernel"]?.Value<string>();
                    info.CPUs = host["cpus"]?.Value<int>() ?? 0;
                    info.MemoryTotal = host["memTotal"]?.Value<long>() ?? 0;

                    var conmon = host["conmon"];
                    if (conmon != null)
                        info.EngineVersion = conmon["version"]?.Value<string>();
                }

                var store = obj["store"];
                if (store != null)
                {
                    info.StorageBackend = store["graphDriverName"]?.Value<string>();
                    info.DataRoot = store["graphRoot"]?.Value<string>();
                    info.Images = store["imageStore"]?["number"]?.Value<int>() ?? 0;
                }

                // Podman version info
                var version = obj["version"];
                if (version != null)
                    info.EngineVersion = version["Version"]?.Value<string>() ?? info.EngineVersion;

                info.OSType = "linux"; // Podman always runs Linux containers
            }
            catch
            {
                // Return partial info on parse failure
            }

            return info;
        }

        private static VersionInfo ParseVersionInfo(string json)
        {
            var version = new VersionInfo();
            try
            {
                var obj = JObject.Parse(json);

                // Podman version JSON: { Client: {...}, Server: {...} } or flat structure
                var client = obj["Client"] ?? obj;
                version.ClientVersion = client["Version"]?.Value<string>();
                version.ClientApiVersion = client["APIVersion"]?.Value<string>();
                version.GitCommit = client["GitCommit"]?.Value<string>();
                version.RuntimeVersion = client["GoVersion"]?.Value<string>();
                version.Os = client["Os"]?.Value<string>() ?? client["OsArch"]?.Value<string>();
                version.Arch = client["Arch"]?.Value<string>();
                version.BuildTime = client["Built"]?.Value<string>();

                var server = obj["Server"];
                if (server != null)
                {
                    version.ServerVersion = server["Version"]?.Value<string>();
                    version.ServerApiVersion = server["APIVersion"]?.Value<string>();
                }
                else
                {
                    // In rootless mode, Server may not be present
                    version.ServerVersion = version.ClientVersion;
                    version.ServerApiVersion = version.ClientApiVersion;
                }

                version.PlatformName = "Podman";
            }
            catch
            {
                // Return partial info on parse failure
            }

            return version;
        }

        #endregion
    }
}
