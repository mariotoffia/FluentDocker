using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of IStreamDriver.
    /// </summary>
    public class DockerCliStreamDriver : DockerCliDriverBase, IStreamDriver
    {
        /// <summary>
        /// Creates a new instance with the specified binary resolver.
        /// </summary>
        public DockerCliStreamDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> StreamLogsAsync(
            DriverContext context,
            string containerId,
            StreamLogsConfig config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            config ??= new StreamLogsConfig();
            var args = "logs -f";
            if (config.Timestamps)
                args += " -t";
            if (config.Tail.HasValue)
                args += $" --tail {config.Tail.Value}";
            if (!string.IsNullOrEmpty(config.Since))
                args += $" --since {config.Since}";
            args += $" {containerId}";

            await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
            {
                yield return line;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ContainerEvent> StreamEventsAsync(
            DriverContext context,
            StreamEventsConfig config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var args = "events --format \"{{json .}}\"";
            if (config?.Since != null)
                args += $" --since {config.Since}";
            if (config?.Until != null)
                args += $" --until {config.Until}";
            if (config?.Filters != null)
            {
                foreach (var filter in config.Filters)
                    args += $" --filter {filter.Key}={filter.Value}";
            }

            await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
            {
                ContainerEvent evt = null;
                try
                {
                    evt = JsonConvert.DeserializeObject<ContainerEvent>(line);
                    if (evt != null)
                        evt.RawJson = line;
                }
                catch
                {
                    // Skip malformed lines
                }

                if (evt != null)
                    yield return evt;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ContainerStats> StreamStatsAsync(
            DriverContext context,
            string containerId = null,
            StreamStatsConfig config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var args = "stats --format \"{{json .}}\"";
            if (config?.All == true)
                args += " -a";
            if (!string.IsNullOrEmpty(containerId))
                args += $" {containerId}";

            await foreach (var line in ExecuteStreamingCommandAsync(args, cancellationToken))
            {
                ContainerStats stats = null;
                try
                {
                    stats = JsonConvert.DeserializeObject<ContainerStats>(line);
                    if (stats != null)
                        stats.RawJson = line;
                }
                catch
                {
                    // Skip malformed lines
                }

                if (stats != null)
                    yield return stats;
            }
        }

        /// <inheritdoc />
        public Task<CommandResponse<AttachResult>> AttachAsync(
            DriverContext context,
            string containerId,
            AttachConfig config = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                config ??= new AttachConfig();
                var args = "attach";

                if (!config.SigProxy)
                    args += " --sig-proxy=false";
                if (!string.IsNullOrEmpty(config.DetachKeys))
                    args += $" --detach-keys {config.DetachKeys}";

                args += $" {containerId}";

                var result = ExecuteAttachProcess(args);
                return Task.FromResult(CommandResponse<AttachResult>.Ok(result));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CommandResponse<AttachResult>.Fail(
                    ex.Message, ErrorCodes.Container.AttachFailed));
            }
        }
    }
}

