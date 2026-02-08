using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Podman CLI implementation of IStreamDriver.
    /// </summary>
    public class PodmanCliStreamDriver : PodmanCliDriverBase, IStreamDriver
    {
        public PodmanCliStreamDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> StreamLogsAsync(
            DriverContext context, string containerId,
            StreamLogsConfig config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var args = $"logs --follow";
            if (config?.Timestamps == true) args += " --timestamps";
            if (config?.Tail.HasValue == true) args += $" --tail {config.Tail.Value}";
            if (!string.IsNullOrEmpty(config?.Since)) args += $" --since {config.Since}";
            if (!string.IsNullOrEmpty(config?.Until)) args += $" --until {config.Until}";
            args += $" {containerId}";

            await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
            {
                yield return line;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
            DriverContext context, StreamEventsConfig config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var args = "events --format json";
            if (!string.IsNullOrEmpty(config?.Since)) args += $" --since {config.Since}";
            if (!string.IsNullOrEmpty(config?.Until)) args += $" --until {config.Until}";

            if (config?.Types != null)
                foreach (var type in config.Types)
                    args += $" --filter type={type}";

            if (config?.Actions != null)
                foreach (var action in config.Actions)
                    args += $" --filter event={action}";

            await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
            {
                var evt = ParseEvent(line);
                if (evt != null) yield return evt;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ContainerStats> StreamStatsAsync(
            DriverContext context, string containerId = null,
            StreamStatsConfig config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var args = "stats --format json";
            if (config?.NoHeader == true) args += " --no-header";
            if (!string.IsNullOrEmpty(containerId)) args += $" {containerId}";

            await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
            {
                var stats = ParseStats(line);
                if (stats != null) yield return stats;
            }
        }

        /// <inheritdoc />
        public Task<CommandResponse<AttachResult>> AttachAsync(
            DriverContext context, string containerId,
            AttachConfig config = null,
            CancellationToken cancellationToken = default)
        {
            // Attach requires interactive I/O which is beyond simple CLI execution.
            // Return a placeholder that callers can use for basic scenarios.
            return Task.FromResult(CommandResponse<AttachResult>.Fail(
                "Attach is not fully supported in CLI mode. Use exec instead.",
                ErrorCodes.Container.AttachFailed));
        }

        #region Parsing

        private static ContainerEvent ParseEvent(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                return new ContainerEvent
                {
                    Type = obj["Type"]?.Value<string>() ?? obj["type"]?.Value<string>(),
                    Action = obj["Action"]?.Value<string>() ?? obj["Status"]?.Value<string>(),
                    ActorId = obj["Actor"]?["ID"]?.Value<string>() ?? obj["id"]?.Value<string>(),
                    RawJson = json
                };
            }
            catch
            {
                return null;
            }
        }

        private static ContainerStats ParseStats(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                return new ContainerStats
                {
                    ContainerId = obj["ContainerID"]?.Value<string>() ?? obj["container_id"]?.Value<string>(),
                    Name = obj["Name"]?.Value<string>() ?? obj["name"]?.Value<string>(),
                    RawJson = json
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
