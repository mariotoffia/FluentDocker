using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of IContainerDriver.
    /// </summary>
    public class DockerCliContainerDriver : DockerCliDriverBase, IContainerDriver
    {
        /// <summary>
        /// Creates a new instance with the specified binary resolver.
        /// </summary>
        public DockerCliContainerDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
        {
        }

        #region Lifecycle Operations

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
            DriverContext context,
            ContainerCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "create" };

                // Name
                if (!string.IsNullOrEmpty(config.Name))
                    args.Add($"--name {config.Name}");

                // Environment variables
                if (config.Environment != null)
                {
                    foreach (var env in config.Environment)
                        args.Add($"-e {env.Key}={env.Value}");
                }

                // Port bindings (container:host)
                if (config.PortBindings != null)
                {
                    foreach (var port in config.PortBindings)
                        args.Add($"-p {port.Value}:{port.Key}");
                }

                // Volume mounts (host:container)
                if (config.Volumes != null)
                {
                    foreach (var volume in config.Volumes)
                        args.Add($"-v {volume.Key}:{volume.Value}");
                }

                // Network mode
                if (!string.IsNullOrEmpty(config.NetworkMode))
                    args.Add($"--network {config.NetworkMode}");

                // Networks
                if (config.Networks != null)
                {
                    foreach (var network in config.Networks)
                        args.Add($"--network {network}");
                }

                // Labels
                if (config.Labels != null)
                {
                    foreach (var label in config.Labels)
                        args.Add($"--label {label.Key}={label.Value}");
                }

                // Working directory
                if (!string.IsNullOrEmpty(config.WorkingDirectory))
                    args.Add($"-w {config.WorkingDirectory}");

                // User
                if (!string.IsNullOrEmpty(config.User))
                    args.Add($"-u {config.User}");

                // Restart policy
                if (!string.IsNullOrEmpty(config.RestartPolicy))
                    args.Add($"--restart {config.RestartPolicy}");

                // Hostname
                if (!string.IsNullOrEmpty(config.Hostname))
                    args.Add($"--hostname {config.Hostname}");

                // Memory limit
                if (config.MemoryLimit.HasValue)
                    args.Add($"--memory {config.MemoryLimit.Value}");

                // CPU shares
                if (config.CpuShares.HasValue)
                    args.Add($"--cpu-shares {config.CpuShares.Value}");

                // Privileged mode
                if (config.Privileged)
                    args.Add("--privileged");

                // Auto remove
                if (config.AutoRemove)
                    args.Add("--rm");

                // Links (legacy Docker feature)
                if (config.Links != null)
                    foreach (var link in config.Links)
                        args.Add($"--link {link}");

                // Image (required)
                args.Add(config.Image);

                // Command - properly quote arguments that contain spaces or special characters
                if (config.Command != null && config.Command.Length > 0)
                {
                    foreach (var cmdArg in config.Command)
                    {
                        args.Add(QuoteArgumentIfNeeded(cmdArg));
                    }
                }

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ContainerCreateResult>.Fail(
                        result.Error ?? "Container creation failed",
                        ErrorCodes.Container.CreateFailed,
                        CreateErrorContext(context, "CreateContainer", result),
                        result.ExitCode);
                }

                var containerId = result.Output.Trim();

                return CommandResponse<ContainerCreateResult>.Ok(
                    new ContainerCreateResult { Id = containerId });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerCreateResult>.Fail(
                    ex.Message,
                    ErrorCodes.Container.CreateFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerRunResult>> RunAsync(
            DriverContext context,
            ContainerCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "run" };

                if (config.Detach)
                    args.Add("-d");

                if (!string.IsNullOrEmpty(config.Name))
                    args.Add($"--name {config.Name}");

                if (config.Environment != null)
                    foreach (var env in config.Environment)
                        args.Add($"-e {env.Key}={env.Value}");

                if (config.PortBindings != null)
                    foreach (var port in config.PortBindings)
                        args.Add($"-p {port.Value}:{port.Key}");

                if (config.Volumes != null)
                    foreach (var volume in config.Volumes)
                        args.Add($"-v {volume.Key}:{volume.Value}");

                if (!string.IsNullOrEmpty(config.NetworkMode))
                    args.Add($"--network {config.NetworkMode}");

                if (config.Labels != null)
                    foreach (var label in config.Labels)
                        args.Add($"--label {label.Key}={label.Value}");

                if (!string.IsNullOrEmpty(config.WorkingDirectory))
                    args.Add($"-w {config.WorkingDirectory}");

                if (!string.IsNullOrEmpty(config.User))
                    args.Add($"-u {config.User}");

                if (!string.IsNullOrEmpty(config.RestartPolicy))
                    args.Add($"--restart {config.RestartPolicy}");

                if (config.Privileged)
                    args.Add("--privileged");

                if (config.AutoRemove)
                    args.Add("--rm");

                // Links (legacy Docker feature)
                if (config.Links != null)
                    foreach (var link in config.Links)
                        args.Add($"--link {link}");

                if (config.Tty)
                    args.Add("-t");

                if (config.Interactive)
                    args.Add("-i");

                // Health check configuration
                if (config.HealthCheck != null)
                {
                    if (config.HealthCheck.Test != null && config.HealthCheck.Test.Length > 0)
                    {
                        // Docker CLI --health-cmd expects just the command, without CMD-SHELL prefix
                        // If the first element is "CMD-SHELL" or "CMD", skip it as it's only for Dockerfile format
                        var testCommands = config.HealthCheck.Test;
                        if (testCommands[0] == "CMD-SHELL" || testCommands[0] == "CMD")
                            testCommands = testCommands.Skip(1).ToArray();
                        
                        var cmd = string.Join(" ", testCommands);
                        args.Add($"--health-cmd \"{cmd}\"");
                    }
                    if (!string.IsNullOrEmpty(config.HealthCheck.Interval))
                        args.Add($"--health-interval {config.HealthCheck.Interval}");
                    if (!string.IsNullOrEmpty(config.HealthCheck.Timeout))
                        args.Add($"--health-timeout {config.HealthCheck.Timeout}");
                    if (config.HealthCheck.Retries > 0)
                        args.Add($"--health-retries {config.HealthCheck.Retries}");
                    if (!string.IsNullOrEmpty(config.HealthCheck.StartPeriod))
                        args.Add($"--health-start-period {config.HealthCheck.StartPeriod}");
                }

                // Memory limit
                if (config.MemoryLimit.HasValue && config.MemoryLimit.Value > 0)
                    args.Add($"--memory {config.MemoryLimit.Value}");

                // CPU shares
                if (config.CpuShares.HasValue && config.CpuShares.Value > 0)
                    args.Add($"--cpu-shares {config.CpuShares.Value}");

                // Hostname
                if (!string.IsNullOrEmpty(config.Hostname))
                    args.Add($"--hostname {config.Hostname}");

                // DNS servers
                if (config.Dns != null)
                    foreach (var dns in config.Dns)
                        args.Add($"--dns {dns}");

                // Extra hosts
                if (config.ExtraHosts != null)
                    foreach (var host in config.ExtraHosts)
                        args.Add($"--add-host {host.Key}:{host.Value}");

                // Entrypoint
                if (config.Entrypoint != null && config.Entrypoint.Length > 0)
                    args.Add($"--entrypoint \"{string.Join(" ", config.Entrypoint)}\"");

                // Stop signal
                if (!string.IsNullOrEmpty(config.StopSignal))
                    args.Add($"--stop-signal {config.StopSignal}");

                // Stop timeout
                if (config.StopTimeout.HasValue)
                    args.Add($"--stop-timeout {config.StopTimeout.Value}");

                args.Add(config.Image);

                // Command - properly quote arguments that contain spaces or special characters
                if (config.Command != null && config.Command.Length > 0)
                {
                    foreach (var cmdArg in config.Command)
                    {
                        args.Add(QuoteArgumentIfNeeded(cmdArg));
                    }
                }

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ContainerRunResult>.Fail(
                        result.Error ?? "Container run failed",
                        ErrorCodes.Container.CreateFailed,
                        CreateErrorContext(context, "RunContainer", result),
                        result.ExitCode);
                }

                var runResult = new ContainerRunResult();

                if (config.Detach)
                {
                    // When detached, output is the container ID
                    runResult.Id = result.Output.Trim();
                }
                else
                {
                    // When not detached, output is the container's stdout/stderr
                    runResult.Output = result.Output;

                    // Find the container ID by listing the most recently created container
                    // Use the name if specified, otherwise get the last created container
                    if (!string.IsNullOrEmpty(config.Name))
                    {
                        var listResult = await ExecuteCommandAsync($"ps -a --filter \"name={config.Name}\" --format \"{{{{.ID}}}}\" -n 1", cancellationToken);
                        if (listResult.Success && !string.IsNullOrEmpty(listResult.Output))
                        {
                            runResult.Id = listResult.Output.Trim().Split('\n')[0];
                        }
                    }
                    else
                    {
                        // Get the most recently created container
                        var listResult = await ExecuteCommandAsync("ps -a --format \"{{.ID}}\" -n 1", cancellationToken);
                        if (listResult.Success && !string.IsNullOrEmpty(listResult.Output))
                        {
                            runResult.Id = listResult.Output.Trim().Split('\n')[0];
                        }
                    }
                }

                return CommandResponse<ContainerRunResult>.Ok(runResult);
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerRunResult>.Fail(ex.Message, ErrorCodes.Container.CreateFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StartAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"start {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container start failed",
                        ErrorCodes.Container.StartFailed,
                        CreateErrorContext(context, "StartContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StartFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> StopAsync(
            DriverContext context,
            string containerId,
            int? timeout = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "stop";
                if (timeout.HasValue)
                    args += $" -t {timeout.Value}";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container stop failed",
                        ErrorCodes.Container.StopFailed,
                        CreateErrorContext(context, "StopContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.StopFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RestartAsync(
            DriverContext context,
            string containerId,
            int? timeout = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "restart";
                if (timeout.HasValue)
                    args += $" -t {timeout.Value}";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container restart failed",
                        ErrorCodes.Container.RestartFailed,
                        CreateErrorContext(context, "RestartContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RestartFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> PauseAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"pause {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container pause failed",
                        ErrorCodes.Container.PauseFailed,
                        CreateErrorContext(context, "PauseContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.PauseFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> UnpauseAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"unpause {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container unpause failed",
                        ErrorCodes.Container.UnpauseFailed,
                        CreateErrorContext(context, "UnpauseContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UnpauseFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> KillAsync(
            DriverContext context,
            string containerId,
            string signal = "SIGKILL",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"kill --signal {signal} {containerId}";
                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container kill failed",
                        ErrorCodes.Container.KillFailed,
                        CreateErrorContext(context, "KillContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.KillFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            string containerId,
            bool force = false,
            bool removeVolumes = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "rm";
                if (force) args += " -f";
                if (removeVolumes) args += " -v";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container remove failed",
                        ErrorCodes.Container.RemoveFailed,
                        CreateErrorContext(context, "RemoveContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RemoveFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerWaitResult>> WaitAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"wait {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ContainerWaitResult>.Fail(
                        result.Error ?? "Container wait failed",
                        ErrorCodes.Container.WaitFailed,
                        CreateErrorContext(context, "WaitContainer", result),
                        result.ExitCode);
                }

                int.TryParse(result.Output.Trim(), out var exitCode);

                return CommandResponse<ContainerWaitResult>.Ok(
                    new ContainerWaitResult { ExitCode = exitCode });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerWaitResult>.Fail(ex.Message, ErrorCodes.Container.WaitFailed);
            }
        }

        #endregion

        #region Information Operations

        /// <inheritdoc />
        public async Task<CommandResponse<Container>> InspectAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"inspect {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Container>.Fail(
                        result.Error ?? "Container inspect failed",
                        ErrorCodes.Container.InspectFailed,
                        CreateErrorContext(context, "InspectContainer", result),
                        result.ExitCode);
                }

                var containers = JsonConvert.DeserializeObject<List<Container>>(result.Output);
                var container = containers?.FirstOrDefault();

                if (container == null)
                {
                    return CommandResponse<Container>.Fail(
                        $"Container {containerId} not found",
                        ErrorCodes.Container.NotFound);
                }

                return CommandResponse<Container>.Ok(container);
            }
            catch (Exception ex)
            {
                return CommandResponse<Container>.Fail(ex.Message, ErrorCodes.Container.InspectFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<IList<Container>>> ListAsync(
            DriverContext context,
            ContainerListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "ps --format \"{{json .}}\"";

                if (filter?.All == true)
                    args += " -a";

                // Add label filters
                if (filter?.Labels != null && filter.Labels.Count > 0)
                {
                    foreach (var label in filter.Labels)
                    {
                        if (string.IsNullOrEmpty(label.Value))
                            args += $" --filter \"label={label.Key}\"";
                        else
                            args += $" --filter \"label={label.Key}={label.Value}\"";
                    }
                }

                // Add name filter
                if (!string.IsNullOrEmpty(filter?.Name))
                    args += $" --filter \"name={filter.Name}\"";

                // Add status filter
                if (!string.IsNullOrEmpty(filter?.Status))
                    args += $" --filter \"status={filter.Status}\"";

                // Add ID filter
                if (!string.IsNullOrEmpty(filter?.Id))
                    args += $" --filter \"id={filter.Id}\"";

                // Add ancestor filter
                if (!string.IsNullOrEmpty(filter?.Ancestor))
                    args += $" --filter \"ancestor={filter.Ancestor}\"";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<Container>>.Fail(
                        result.Error ?? "Container list failed",
                        ErrorCodes.General.Unknown);
                }

                var containers = new List<Container>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        // Docker ps JSON has different field names than our Container model
                        var dto = JsonConvert.DeserializeObject<DockerPsDto>(line);
                        if (dto != null)
                        {
                            var container = new Container
                            {
                                Id = dto.ID,
                                Image = dto.Image,
                                Name = dto.Names
                            };

                            // Parse CreatedAt if present
                            if (!string.IsNullOrEmpty(dto.CreatedAt) && DateTime.TryParse(dto.CreatedAt, out var created))
                            {
                                container.Created = created;
                            }

                            // Parse State if present
                            if (!string.IsNullOrEmpty(dto.State))
                            {
                                container.State = new ContainerState
                                {
                                    Running = dto.State.Equals("running", StringComparison.OrdinalIgnoreCase),
                                    Status = dto.Status
                                };
                            }

                            containers.Add(container);
                        }
                    }
                    catch
                    {
                        // Skip invalid JSON lines
                    }
                }

                return CommandResponse<IList<Container>>.Ok(containers);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<Container>>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            string containerId,
            bool follow = false,
            int? tail = null,
            bool timestamps = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "logs";
                if (follow)
                    args += " -f";
                if (tail.HasValue)
                    args += $" --tail {tail.Value}";
                if (timestamps)
                    args += " -t";
                args += $" {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<string>.Fail(
                        result.Error ?? "Get logs failed",
                        ErrorCodes.General.Unknown);
                }

                return CommandResponse<string>.Ok(result.Output);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<ContainerProcesses>> TopAsync(
            DriverContext context,
            string containerId,
            string psOptions = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"top {containerId}";
                if (!string.IsNullOrEmpty(psOptions))
                    args += $" {psOptions}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ContainerProcesses>.Fail(
                        result.Error ?? "Container top failed",
                        ErrorCodes.Container.TopFailed,
                        CreateErrorContext(context, "TopContainer", result),
                        result.ExitCode);
                }

                var processes = new ContainerProcesses();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    processes.Titles = lines[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        processes.Processes.Add(lines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList());
                    }
                }

                return CommandResponse<ContainerProcesses>.Ok(processes);
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerProcesses>.Fail(ex.Message, ErrorCodes.Container.TopFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"diff {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<FilesystemChange>>.Fail(
                        result.Error ?? "Container diff failed",
                        ErrorCodes.Container.DiffFailed,
                        CreateErrorContext(context, "DiffContainer", result),
                        result.ExitCode);
                }

                var changes = new List<FilesystemChange>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Length > 2)
                    {
                        changes.Add(new FilesystemChange
                        {
                            Kind = line.Substring(0, 1),
                            Path = line.Substring(2)
                        });
                    }
                }

                return CommandResponse<IList<FilesystemChange>>.Ok(changes);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<FilesystemChange>>.Fail(ex.Message, ErrorCodes.Container.DiffFailed);
            }
        }

        #endregion

        #region Execution Operations

        /// <inheritdoc />
        public async Task<CommandResponse<ExecResult>> ExecAsync(
            DriverContext context,
            string containerId,
            ExecConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "exec" };

                if (config.Detach)
                    args.Add("-d");
                if (config.Interactive)
                    args.Add("-i");
                if (config.Tty)
                    args.Add("-t");
                if (config.Privileged)
                    args.Add("--privileged");
                if (!string.IsNullOrEmpty(config.User))
                    args.Add($"-u {config.User}");
                if (!string.IsNullOrEmpty(config.WorkingDir))
                    args.Add($"-w {config.WorkingDir}");
                if (config.Environment != null)
                    foreach (var env in config.Environment)
                        args.Add($"-e {env.Key}={env.Value}");

                args.Add(containerId);
                if (config.Command != null)
                    args.AddRange(config.Command);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                return CommandResponse<ExecResult>.Ok(new ExecResult
                {
                    ExitCode = result.ExitCode,
                    StdOut = result.Output,
                    StdErr = result.Error
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ExecResult>.Fail(ex.Message, ErrorCodes.Container.ExecFailed);
            }
        }

        #endregion

        #region Copy Operations

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> CopyToAsync(
            DriverContext context,
            string containerId,
            string hostPath,
            string containerPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"cp \"{hostPath}\" {containerId}:{containerPath}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Copy to container failed",
                        ErrorCodes.Container.CopyFailed,
                        CreateErrorContext(context, "CopyToContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.CopyFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> CopyFromAsync(
            DriverContext context,
            string containerId,
            string containerPath,
            string hostPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"cp {containerId}:{containerPath} \"{hostPath}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Copy from container failed",
                        ErrorCodes.Container.CopyFailed,
                        CreateErrorContext(context, "CopyFromContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
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
            DriverContext context,
            string containerId,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"export -o \"{outputPath}\" {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container export failed",
                        ErrorCodes.Container.ExportFailed,
                        CreateErrorContext(context, "ExportContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.ExportFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> RenameAsync(
            DriverContext context,
            string containerId,
            string newName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"rename {containerId} {newName}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container rename failed",
                        ErrorCodes.Container.RenameFailed,
                        CreateErrorContext(context, "RenameContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.RenameFailed);
            }
        }

        /// <inheritdoc />
        public async Task<CommandResponse<Unit>> UpdateAsync(
            DriverContext context,
            string containerId,
            ContainerUpdateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "update" };

                if (config.MemoryLimit.HasValue)
                    args.Add($"--memory {config.MemoryLimit.Value}");
                if (config.MemoryReservation.HasValue)
                    args.Add($"--memory-reservation {config.MemoryReservation.Value}");
                if (config.CpuShares.HasValue)
                    args.Add($"--cpu-shares {config.CpuShares.Value}");
                if (config.CpuPeriod.HasValue)
                    args.Add($"--cpu-period {config.CpuPeriod.Value}");
                if (config.CpuQuota.HasValue)
                    args.Add($"--cpu-quota {config.CpuQuota.Value}");
                if (!string.IsNullOrEmpty(config.CpusetCpus))
                    args.Add($"--cpuset-cpus {config.CpusetCpus}");
                if (!string.IsNullOrEmpty(config.RestartPolicy))
                    args.Add($"--restart {config.RestartPolicy}");
                if (config.PidsLimit.HasValue)
                    args.Add($"--pids-limit {config.PidsLimit.Value}");

                args.Add(containerId);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Container update failed",
                        ErrorCodes.Container.UpdateFailed,
                        CreateErrorContext(context, "UpdateContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Container.UpdateFailed);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Quotes an argument if it contains spaces.
        /// For ProcessStartInfo on Unix, arguments containing spaces need to be quoted.
        /// We use double quotes and escape any existing double quotes.
        /// </summary>
        /// <param name="argument">The argument to potentially quote</param>
        /// <returns>The argument, quoted if necessary</returns>
        private static string QuoteArgumentIfNeeded(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return argument;

            // Only quote if the argument contains spaces or tabs
            // Other special characters are handled correctly by ProcessStartInfo
            bool needsQuoting = argument.Contains(' ') || argument.Contains('\t');

            if (!needsQuoting)
                return argument;

            // Escape any backslashes first, then escape double quotes
            var escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }

        /// <summary>
        /// DTO for docker ps JSON output.
        /// </summary>
        private class DockerPsDto
        {
            public string ID { get; set; }
            public string Image { get; set; }
            public string Command { get; set; }
            public string CreatedAt { get; set; }
            public string Names { get; set; }
            public string State { get; set; }
            public string Status { get; set; }
            public string Ports { get; set; }
            public string Labels { get; set; }
            public string Mounts { get; set; }
            public string Networks { get; set; }
            public string RunningFor { get; set; }
            public string Size { get; set; }
        }

        #endregion
    }
}

