using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Docker.Api.Components
{
    /// <summary>
    /// Docker API implementation of ISystemDriver.
    /// Uses GET /info, GET /version, GET /_ping, GET /system/df, POST /system/prune.
    /// </summary>
    public class DockerApiSystemDriver : DockerApiDriverBase, ISystemDriver
    {
        public DockerApiSystemDriver(IDockerApiConnection connection) : base(connection) { }

        public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var result = await GetJsonAsync<JObject>("/info", cancellationToken);
            if (!result.Success)
                return CommandResponse<SystemInfo>.Fail(result.ErrorMessage,
                    MapHttpErrorCode(result.StatusCode),
                    CreateErrorContext("GET /info", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var info = ParseSystemInfo(result.Data);
            return CommandResponse<SystemInfo>.Ok(info);
        }

        public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var result = await GetJsonAsync<JObject>("/version", cancellationToken);
            if (!result.Success)
                return CommandResponse<VersionInfo>.Fail(result.ErrorMessage,
                    MapHttpErrorCode(result.StatusCode),
                    CreateErrorContext("GET /version", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var version = ParseVersionInfo(result.Data);
            return CommandResponse<VersionInfo>.Ok(version);
        }

        public async Task<CommandResponse<Unit>> PingAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var ok = await Connection.PingAsync(cancellationToken);
            return ok
                ? CommandResponse<Unit>.Ok(Unit.Default)
                : CommandResponse<Unit>.Fail("Docker daemon is not responding",
                    ErrorCodes.Driver.NotAvailable);
        }

        public async Task<CommandResponse<bool>> IsWindowsEngineAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var infoResult = await GetInfoAsync(context, cancellationToken);
            if (!infoResult.Success)
                return CommandResponse<bool>.Fail(infoResult.Error, infoResult.ErrorCode);
            return CommandResponse<bool>.Ok(
                infoResult.Data.OSType?.ToLowerInvariant() == "windows");
        }

        public async Task<CommandResponse<bool>> IsLinuxEngineAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var infoResult = await GetInfoAsync(context, cancellationToken);
            if (!infoResult.Success)
                return CommandResponse<bool>.Fail(infoResult.Error, infoResult.ErrorCode);
            return CommandResponse<bool>.Ok(
                infoResult.Data.OSType?.ToLowerInvariant() == "linux");
        }

        public async Task<CommandResponse<DiskUsageInfo>> GetDiskUsageAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            var result = await GetJsonAsync<JObject>("/system/df", cancellationToken);
            if (!result.Success)
                return CommandResponse<DiskUsageInfo>.Fail(result.ErrorMessage,
                    MapHttpErrorCode(result.StatusCode),
                    CreateErrorContext("GET /system/df", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var usage = ParseDiskUsageInfo(result.Data);
            return CommandResponse<DiskUsageInfo>.Ok(usage);
        }

        public async Task<CommandResponse<SystemPruneResult>> PruneAsync(
            DriverContext context, SystemPruneConfig config = null,
            CancellationToken cancellationToken = default)
        {
            var path = "/system/prune";
            var result = await PostJsonAsync<JObject>(path, null, cancellationToken);
            if (!result.Success)
                return CommandResponse<SystemPruneResult>.Fail(result.ErrorMessage,
                    MapHttpErrorCode(result.StatusCode),
                    CreateErrorContext("POST /system/prune", result.StatusCode, result.ResponseBody),
                    result.StatusCode);

            var pruneResult = new SystemPruneResult();
            if (result.Data != null)
            {
                pruneResult.SpaceReclaimed = result.Data.Value<long?>("SpaceReclaimed") ?? 0;
            }

            return CommandResponse<SystemPruneResult>.Ok(pruneResult);
        }

        public Task<CommandResponse<Unit>> SwitchDaemonAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Daemon switching is not supported via the Docker API",
                ErrorCodes.Driver.CapabilityNotSupported));
        }

        public Task<CommandResponse<Unit>> SwitchToLinuxDaemonAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Daemon switching is not supported via the Docker API",
                ErrorCodes.Driver.CapabilityNotSupported));
        }

        public Task<CommandResponse<Unit>> SwitchToWindowsDaemonAsync(
            DriverContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Daemon switching is not supported via the Docker API",
                ErrorCodes.Driver.CapabilityNotSupported));
        }

        #region JSON Parsing

        private static SystemInfo ParseSystemInfo(JObject json)
        {
            if (json == null) return new SystemInfo();

            var info = new SystemInfo
            {
                OperatingSystem = json.Value<string>("OperatingSystem"),
                OSType = json.Value<string>("OSType"),
                OSVersion = json.Value<string>("OSVersion"),
                Architecture = json.Value<string>("Architecture"),
                Containers = json.Value<int?>("Containers") ?? 0,
                ContainersRunning = json.Value<int?>("ContainersRunning") ?? 0,
                ContainersPaused = json.Value<int?>("ContainersPaused") ?? 0,
                ContainersStopped = json.Value<int?>("ContainersStopped") ?? 0,
                Images = json.Value<int?>("Images") ?? 0,
                EngineVersion = json.Value<string>("ServerVersion"),
                StorageBackend = json.Value<string>("Driver"),
                LoggingBackend = json.Value<string>("LoggingDriver"),
                KernelVersion = json.Value<string>("KernelVersion"),
                MemoryTotal = json.Value<long?>("MemTotal") ?? 0,
                CPUs = json.Value<int?>("NCPU") ?? 0,
                DataRoot = json.Value<string>("DockerRootDir"),
                Hostname = json.Value<string>("Name"),
                DefaultRuntime = json.Value<string>("DefaultRuntime"),
            };

            info.PopulateMeta();
            return info;
        }

        private static VersionInfo ParseVersionInfo(JObject json)
        {
            if (json == null) return new VersionInfo();

            var version = new VersionInfo
            {
                ServerVersion = json.Value<string>("Version"),
                ServerApiVersion = json.Value<string>("ApiVersion"),
                MinApiVersion = json.Value<string>("MinAPIVersion"),
                GitCommit = json.Value<string>("GitCommit"),
                RuntimeVersion = json.Value<string>("GoVersion"),
                Os = json.Value<string>("Os"),
                Arch = json.Value<string>("Arch"),
                BuildTime = json.Value<string>("BuildTime"),
                Experimental = json.Value<bool?>("Experimental") ?? false,
            };

            var platform = json["Platform"] as JObject;
            version.PlatformName = platform?.Value<string>("Name");

            // For API driver, client version = server version (direct API access)
            version.ClientVersion = version.ServerVersion;
            version.ClientApiVersion = version.ServerApiVersion;

            version.PopulateMeta();
            return version;
        }

        private static DiskUsageInfo ParseDiskUsageInfo(JObject json)
        {
            if (json == null) return new DiskUsageInfo();

            var usage = new DiskUsageInfo();
            long totalSize = 0;

            if (json["Images"] is JArray images)
            {
                usage.Images.TotalCount = images.Count;
                foreach (var img in images)
                {
                    var size = img.Value<long?>("Size") ?? 0;
                    usage.Images.Size += size;
                    totalSize += size;
                }
            }

            if (json["Containers"] is JArray containers)
            {
                usage.Containers.TotalCount = containers.Count;
                foreach (var c in containers)
                {
                    var size = c.Value<long?>("SizeRw") ?? 0;
                    usage.Containers.Size += size;
                    totalSize += size;
                }
            }

            if (json["Volumes"] is JArray volumes)
            {
                usage.Volumes.TotalCount = volumes.Count;
                foreach (var v in volumes)
                {
                    var size = v.Value<long?>("UsageData")?? 0;
                    usage.Volumes.Size += size;
                    totalSize += size;
                }
            }

            usage.TotalSize = totalSize;
            return usage;
        }

        #endregion
    }
}
