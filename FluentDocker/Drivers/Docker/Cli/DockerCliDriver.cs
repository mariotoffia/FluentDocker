using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Commands;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Images;
using FluentDocker.Model.Networks;
using FluentDocker.Model.Volumes;
using FluentDocker.Services;
using Newtonsoft.Json;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Drivers.Docker.Cli
{
    /// <summary>
    /// Docker CLI driver implementation for v3.0.0.
    /// Implements all driver interfaces for complete Docker CLI support.
    /// </summary>
    public class DockerCliDriver : IDriver, IContainerDriver, IImageDriver, INetworkDriver, IVolumeDriver, 
                                   ISystemDriver, IComposeDriver, IAuthDriver, IStreamDriver, 
                                   IStackDriver, IServiceDriver, IMachineDriver
    {
        private const string DockerCommand = "docker";
        private DriverContext _context;

        public DriverType Type => DriverType.DockerCli;
        public RuntimeType Runtime => RuntimeType.Docker;

        public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DriverCapabilities
            {
                SupportsContainers = true,
                SupportsImages = true,
                SupportsNetworks = true,
                SupportsVolumes = true,
                SupportsCompose = true,
                SupportsSystem = true,
                SupportsPods = false
            });
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Use simple version check without format to avoid shell escaping issues
                var result = await ExecuteCommandAsync("version", cancellationToken);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            return Task.CompletedTask;
        }

        // IContainerDriver implementation
        public async Task<Model.Drivers.CommandResponse<ContainerCreateResult>> CreateAsync(
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

                // Image (required)
                args.Add(config.Image);

                // Command
                if (config.Command != null && config.Command.Length > 0)
                    args.AddRange(config.Command);

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

        public async Task<Model.Drivers.CommandResponse<Unit>> StartAsync(
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

        public async Task<Model.Drivers.CommandResponse<Unit>> StopAsync(
            DriverContext context,
            string containerId,
            int? timeout = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"stop";
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

        // Old RemoveAsync removed - now using the public RemoveAsync with removeVolumes parameter

        Task<Model.Drivers.CommandResponse<Container>> IContainerDriver.InspectAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken)
        {
            return InspectContainerAsync(context, containerId, cancellationToken);
        }

        private async Task<CommandResponse<Container>> InspectContainerAsync(
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken)
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

        public async Task<Model.Drivers.CommandResponse<IList<Container>>> ListAsync(
            DriverContext context,
            ContainerListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "ps --format \"{{json .}}\"";

                if (filter?.All == true)
                    args += " -a";

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
                        var container = JsonConvert.DeserializeObject<Container>(line);
                        if (container != null)
                            containers.Add(container);
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

        public async Task<Model.Drivers.CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            string containerId,
            bool follow = false,
            int? tail = null,
            bool timestamps = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"logs";
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

        // === New IContainerDriver methods ===

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

                if (config.Tty)
                    args.Add("-t");

                if (config.Interactive)
                    args.Add("-i");

                args.Add(config.Image);

                if (config.Command != null && config.Command.Length > 0)
                    args.AddRange(config.Command);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ContainerRunResult>.Fail(
                        result.Error ?? "Container run failed",
                        ErrorCodes.Container.CreateFailed,
                        CreateErrorContext(context, "RunContainer", result),
                        result.ExitCode);
                }

                return CommandResponse<ContainerRunResult>.Ok(
                    new ContainerRunResult { Id = result.Output.Trim() });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerRunResult>.Fail(ex.Message, ErrorCodes.Container.CreateFailed);
            }
        }

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

                int exitCode;
                int.TryParse(result.Output.Trim(), out exitCode);

                return CommandResponse<ContainerWaitResult>.Ok(
                    new ContainerWaitResult { ExitCode = exitCode });
            }
            catch (Exception ex)
            {
                return CommandResponse<ContainerWaitResult>.Fail(ex.Message, ErrorCodes.Container.WaitFailed);
            }
        }

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

        // IImageDriver implementation
        public async Task<CommandResponse<Unit>> PullAsync(
            DriverContext context,
            string image,
            string tag = "latest",
            IProgress<ImagePullProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var fullImage = string.IsNullOrEmpty(tag) ? image : $"{image}:{tag}";
                var result = await ExecuteCommandAsync($"pull {fullImage}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Image pull failed",
                        ErrorCodes.Image.PullFailed,
                        CreateErrorContext(context, "PullImage", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PullFailed);
            }
        }

        // Old RemoveImageAsync removed - now using RemoveImageWithResultAsync

        public async Task<CommandResponse<ImageBuildResult>> BuildAsync(
            DriverContext context,
            ImageBuildConfig config,
            IProgress<ImageBuildProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "build" };

                if (!string.IsNullOrEmpty(config.DockerfileName))
                    args.Add($"--file {config.DockerfileName}");

                foreach (var tag in config.Tags)
                    args.Add($"--tag {tag}");

                foreach (var buildArg in config.BuildArgs)
                    args.Add($"--build-arg {buildArg.Key}={buildArg.Value}");

                foreach (var label in config.Labels)
                    args.Add($"--label {label.Key}={label.Value}");

                if (!string.IsNullOrEmpty(config.Target))
                    args.Add($"--target {config.Target}");

                args.Add(config.BuildContext ?? ".");

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ImageBuildResult>.Fail(
                        result.Error ?? "Image build failed",
                        ErrorCodes.Image.BuildFailed,
                        CreateErrorContext(context, "BuildImage", result),
                        result.ExitCode);
                }

                // Extract image ID from build output (usually last line with "sha256:...")
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var imageId = "";
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (line.Contains("sha256:"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        imageId = parts.Where(p => p.StartsWith("sha256:")).LastOrDefault() ?? "";
                        break;
                    }
                }

                return CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
                {
                    ImageId = imageId,
                    Warnings = new List<string>()
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ImageBuildResult>.Fail(ex.Message, ErrorCodes.Image.BuildFailed);
            }
        }

        Task<CommandResponse<IList<Image>>> IImageDriver.ListAsync(
            DriverContext context,
            ImageListFilter filter,
            CancellationToken cancellationToken)
        {
            return ListImagesAsync(context, filter, cancellationToken);
        }

        private async Task<CommandResponse<IList<Image>>> ListImagesAsync(
            DriverContext context,
            ImageListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "images --format \"{{json .}}\"";

                if (filter?.All == true)
                    args += " -a";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<Image>>.Fail(
                        result.Error ?? "Image list failed",
                        ErrorCodes.General.Unknown);
                }

                var images = new List<Image>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        var image = JsonConvert.DeserializeObject<Image>(line);
                        if (image != null)
                            images.Add(image);
                    }
                    catch
                    {
                        // Skip invalid JSON lines
                    }
                }

                return CommandResponse<IList<Image>>.Ok(images);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<Image>>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        Task<CommandResponse<Image>> IImageDriver.InspectAsync(
            DriverContext context,
            string imageId,
            CancellationToken cancellationToken)
        {
            return InspectImageAsync(context, imageId, cancellationToken);
        }

        private async Task<CommandResponse<Image>> InspectImageAsync(
            DriverContext context,
            string imageId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"image inspect {imageId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Image>.Fail(
                        result.Error ?? "Image inspect failed",
                        ErrorCodes.Image.InspectFailed);
                }

                var images = JsonConvert.DeserializeObject<List<Image>>(result.Output);
                var image = images?.FirstOrDefault();

                if (image == null)
                {
                    return CommandResponse<Image>.Fail(
                        $"Image {imageId} not found",
                        ErrorCodes.Image.NotFound);
                }

                return CommandResponse<Image>.Ok(image);
            }
            catch (Exception ex)
            {
                return CommandResponse<Image>.Fail(ex.Message, ErrorCodes.Image.InspectFailed);
            }
        }

        public async Task<CommandResponse<Unit>> TagAsync(
            DriverContext context,
            string imageId,
            string repository,
            string tag,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"tag {imageId} {repository}:{tag}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Image tag failed",
                        ErrorCodes.Image.TagFailed);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.TagFailed);
            }
        }

        // === New IImageDriver methods ===

        public async Task<CommandResponse<Unit>> PushAsync(
            DriverContext context,
            string image,
            IProgress<ImagePushProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"push {image}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Image push failed",
                        ErrorCodes.Image.PushFailed,
                        CreateErrorContext(context, "PushImage", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PushFailed);
            }
        }

        public async Task<CommandResponse<IList<ImageLayer>>> HistoryAsync(
            DriverContext context,
            string imageId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"history --format '{{{{json .}}}}' --no-trunc {imageId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<ImageLayer>>.Fail(
                        result.Error ?? "Image history failed",
                        ErrorCodes.Image.HistoryFailed,
                        CreateErrorContext(context, "HistoryImage", result),
                        result.ExitCode);
                }

                var layers = new List<ImageLayer>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var layer = JsonConvert.DeserializeObject<ImageLayer>(line);
                        if (layer != null)
                            layers.Add(layer);
                    }
                    catch { }
                }

                return CommandResponse<IList<ImageLayer>>.Ok(layers);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ImageLayer>>.Fail(ex.Message, ErrorCodes.Image.HistoryFailed);
            }
        }

        public async Task<CommandResponse<Unit>> SaveAsync(
            DriverContext context,
            string[] images,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"save -o \"{outputPath}\" {string.Join(" ", images)}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Image save failed",
                        ErrorCodes.Image.SaveFailed,
                        CreateErrorContext(context, "SaveImage", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.SaveFailed);
            }
        }

        public async Task<CommandResponse<IList<string>>> LoadAsync(
            DriverContext context,
            string inputPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"load -i \"{inputPath}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<string>>.Fail(
                        result.Error ?? "Image load failed",
                        ErrorCodes.Image.LoadFailed,
                        CreateErrorContext(context, "LoadImage", result),
                        result.ExitCode);
                }

                // Parse loaded image names from output
                var images = new List<string>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("Loaded image:"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1)
                            images.Add(parts[1].Trim());
                    }
                }

                return CommandResponse<IList<string>>.Ok(images);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<string>>.Fail(ex.Message, ErrorCodes.Image.LoadFailed);
            }
        }

        public async Task<CommandResponse<string>> ImportAsync(
            DriverContext context,
            string source,
            string repository = null,
            string tag = null,
            string message = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"import";
                if (!string.IsNullOrEmpty(message))
                    args += $" -m \"{message}\"";
                args += $" \"{source}\"";
                if (!string.IsNullOrEmpty(repository))
                {
                    args += $" {repository}";
                    if (!string.IsNullOrEmpty(tag))
                        args += $":{tag}";
                }

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<string>.Fail(
                        result.Error ?? "Image import failed",
                        ErrorCodes.Image.ImportFailed,
                        CreateErrorContext(context, "ImportImage", result),
                        result.ExitCode);
                }

                return CommandResponse<string>.Ok(result.Output.Trim());
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Image.ImportFailed);
            }
        }

        public async Task<CommandResponse<ImagePruneResult>> PruneAsync(
            DriverContext context,
            bool all = false,
            Dictionary<string, string> filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "image prune -f";
                if (all)
                    args += " -a";
                if (filter != null)
                    foreach (var f in filter)
                        args += $" --filter {f.Key}={f.Value}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ImagePruneResult>.Fail(
                        result.Error ?? "Image prune failed",
                        ErrorCodes.Image.PruneFailed,
                        CreateErrorContext(context, "PruneImages", result),
                        result.ExitCode);
                }

                return CommandResponse<ImagePruneResult>.Ok(new ImagePruneResult());
            }
            catch (Exception ex)
            {
                return CommandResponse<ImagePruneResult>.Fail(ex.Message, ErrorCodes.Image.PruneFailed);
            }
        }

        Task<CommandResponse<ImageRemoveResult>> IImageDriver.RemoveAsync(
            DriverContext context,
            string imageId,
            bool force,
            bool noPrune,
            CancellationToken cancellationToken)
        {
            return RemoveImageWithResultAsync(context, imageId, force, noPrune, cancellationToken);
        }

        private async Task<CommandResponse<ImageRemoveResult>> RemoveImageWithResultAsync(
            DriverContext context,
            string imageId,
            bool force = false,
            bool noPrune = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "rmi";
                if (force)
                    args += " -f";
                if (noPrune)
                    args += " --no-prune";
                args += $" {imageId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ImageRemoveResult>.Fail(
                        result.Error ?? "Image remove failed",
                        ErrorCodes.Image.RemoveFailed);
                }

                // Parse removed/untagged images from output
                var removeResult = new ImageRemoveResult();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Deleted:"))
                        removeResult.Deleted.Add(line.Substring(8).Trim());
                    else if (line.StartsWith("Untagged:"))
                        removeResult.Untagged.Add(line.Substring(9).Trim());
                }

                return CommandResponse<ImageRemoveResult>.Ok(removeResult);
            }
            catch (Exception ex)
            {
                return CommandResponse<ImageRemoveResult>.Fail(ex.Message, ErrorCodes.Image.RemoveFailed);
            }
        }

        // ========== INetworkDriver Implementation ==========

        public async Task<CommandResponse<NetworkCreateResult>> CreateAsync(
            DriverContext context,
            NetworkCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "network", "create" };

                if (!string.IsNullOrEmpty(config.Driver))
                    args.Add($"--driver {config.Driver}");

                if (!string.IsNullOrEmpty(config.Subnet))
                    args.Add($"--subnet {config.Subnet}");

                if (!string.IsNullOrEmpty(config.Gateway))
                    args.Add($"--gateway {config.Gateway}");

                if (config.EnableIPv6)
                    args.Add("--ipv6");

                if (config.Labels != null)
                {
                    foreach (var label in config.Labels)
                        args.Add($"--label {label.Key}={label.Value}");
                }

                args.Add(config.Name);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<NetworkCreateResult>.Fail(
                        result.Error ?? "Network creation failed",
                        ErrorCodes.Network.CreateFailed,
                        CreateErrorContext(context, "CreateNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<NetworkCreateResult>.Ok(
                    new NetworkCreateResult { Id = result.Output.Trim() });
            }
            catch (Exception ex)
            {
                return CommandResponse<NetworkCreateResult>.Fail(ex.Message, ErrorCodes.Network.CreateFailed);
            }
        }

        Task<CommandResponse<Unit>> INetworkDriver.RemoveAsync(DriverContext context, string networkId, CancellationToken cancellationToken)
        {
            return RemoveNetworkAsync(context, networkId, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> RemoveNetworkAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync($"network rm {networkId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Network removal failed",
                        ErrorCodes.Network.RemoveFailed,
                        CreateErrorContext(context, "RemoveNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.RemoveFailed);
            }
        }

        Task<CommandResponse<IList<Network>>> INetworkDriver.ListAsync(DriverContext context, NetworkListFilter filter, CancellationToken cancellationToken)
        {
            return ListNetworksAsync(context, filter, cancellationToken);
        }

        private async Task<CommandResponse<IList<Network>>> ListNetworksAsync(
            DriverContext context,
            NetworkListFilter filter,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync("network ls --format \"{{json .}}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<Network>>.Fail(
                        result.Error ?? "Network list failed",
                        ErrorCodes.General.Unknown,
                        CreateErrorContext(context, "ListNetworks", result),
                        result.ExitCode);
                }

                var networks = new List<Network>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        var network = JsonConvert.DeserializeObject<Network>(line);
                        if (network != null)
                            networks.Add(network);
                    }
                    catch { /* Skip malformed lines */ }
                }

                return CommandResponse<IList<Network>>.Ok(networks);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<Network>>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        Task<CommandResponse<Network>> INetworkDriver.InspectAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken)
        {
            return InspectNetworkAsync(context, networkId, cancellationToken);
        }

        private async Task<CommandResponse<Network>> InspectNetworkAsync(
            DriverContext context,
            string networkId,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync($"network inspect {networkId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Network>.Fail(
                        result.Error ?? "Network inspect failed",
                        ErrorCodes.Network.InspectFailed,
                        CreateErrorContext(context, "InspectNetwork", result),
                        result.ExitCode);
                }

                var networks = JsonConvert.DeserializeObject<List<Network>>(result.Output);
                return CommandResponse<Network>.Ok(networks?.FirstOrDefault() ?? new Network());
            }
            catch (Exception ex)
            {
                return CommandResponse<Network>.Fail(ex.Message, ErrorCodes.Network.InspectFailed);
            }
        }

        public async Task<CommandResponse<Unit>> ConnectAsync(DriverContext context, string networkId, string containerId, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync($"network connect {networkId} {containerId}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Network connect failed",
                        ErrorCodes.Network.ConnectFailed,
                        CreateErrorContext(context, "ConnectNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.ConnectFailed);
            }
        }

        public async Task<CommandResponse<Unit>> DisconnectAsync(DriverContext context, string networkId, string containerId, bool force = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"network disconnect";
                if (force)
                    args += " --force";
                args += $" {networkId} {containerId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Network disconnect failed",
                        ErrorCodes.Network.DisconnectFailed,
                        CreateErrorContext(context, "DisconnectNetwork", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Network.DisconnectFailed);
            }
        }

        Task<CommandResponse<NetworkPruneResult>> INetworkDriver.PruneAsync(
            DriverContext context,
            CancellationToken cancellationToken)
        {
            return PruneNetworkAsync(context, cancellationToken);
        }

        private async Task<CommandResponse<NetworkPruneResult>> PruneNetworkAsync(
            DriverContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync("network prune --force", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<NetworkPruneResult>.Fail(
                        result.Error ?? "Network prune failed",
                        ErrorCodes.Network.PruneFailed);
                }

                return CommandResponse<NetworkPruneResult>.Ok(new NetworkPruneResult());
            }
            catch (Exception ex)
            {
                return CommandResponse<NetworkPruneResult>.Fail(ex.Message, ErrorCodes.Network.PruneFailed);
            }
        }

        // ========== IVolumeDriver Implementation ==========

        public async Task<CommandResponse<VolumeCreateResult>> CreateAsync(DriverContext context, VolumeCreateConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "volume", "create" };

                if (!string.IsNullOrEmpty(config.Driver))
                    args.Add($"--driver {config.Driver}");

                if (config.Labels != null)
                {
                    foreach (var label in config.Labels)
                        args.Add($"--label {label.Key}={label.Value}");
                }

                if (config.DriverOpts != null)
                {
                    foreach (var opt in config.DriverOpts)
                        args.Add($"--opt {opt.Key}={opt.Value}");
                }

                args.Add(config.Name);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<VolumeCreateResult>.Fail(
                        result.Error ?? "Volume creation failed",
                        ErrorCodes.Volume.CreateFailed,
                        CreateErrorContext(context, "CreateVolume", result),
                        result.ExitCode);
                }

                return CommandResponse<VolumeCreateResult>.Ok(
                    new VolumeCreateResult { Name = result.Output.Trim() });
            }
            catch (Exception ex)
            {
                return CommandResponse<VolumeCreateResult>.Fail(ex.Message, ErrorCodes.Volume.CreateFailed);
            }
        }

        Task<CommandResponse<Unit>> IVolumeDriver.RemoveAsync(DriverContext context, string volumeName, bool force, CancellationToken cancellationToken)
        {
            return RemoveVolumeAsync(context, volumeName, force, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> RemoveVolumeAsync(
            DriverContext context,
            string volumeName,
            bool force,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = "volume rm";
                if (force)
                    args += " --force";
                args += $" {volumeName}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Volume removal failed",
                        ErrorCodes.Volume.RemoveFailed,
                        CreateErrorContext(context, "RemoveVolume", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Volume.RemoveFailed);
            }
        }

        Task<CommandResponse<IList<Volume>>> IVolumeDriver.ListAsync(DriverContext context, VolumeListFilter filter, CancellationToken cancellationToken)
        {
            return ListVolumesAsync(context, filter, cancellationToken);
        }

        private async Task<CommandResponse<IList<Volume>>> ListVolumesAsync(
            DriverContext context,
            VolumeListFilter filter,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync("volume ls --format \"{{json .}}\"", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<Volume>>.Fail(
                        result.Error ?? "Volume list failed",
                        ErrorCodes.General.Unknown,
                        CreateErrorContext(context, "ListVolumes", result),
                        result.ExitCode);
                }

                var volumes = new List<Volume>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        var volume = JsonConvert.DeserializeObject<Volume>(line);
                        if (volume != null)
                            volumes.Add(volume);
                    }
                    catch { /* Skip malformed lines */ }
                }

                return CommandResponse<IList<Volume>>.Ok(volumes);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<Volume>>.Fail(ex.Message, ErrorCodes.General.Unknown);
            }
        }

        Task<CommandResponse<Volume>> IVolumeDriver.InspectAsync(DriverContext context, string volumeName, CancellationToken cancellationToken)
        {
            return InspectVolumeAsync(context, volumeName, cancellationToken);
        }

        private async Task<CommandResponse<Volume>> InspectVolumeAsync(
            DriverContext context,
            string volumeName,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync($"volume inspect {volumeName}", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Volume>.Fail(
                        result.Error ?? "Volume inspect failed",
                        ErrorCodes.Volume.InspectFailed,
                        CreateErrorContext(context, "InspectVolume", result),
                        result.ExitCode);
                }

                var volumes = JsonConvert.DeserializeObject<List<Volume>>(result.Output);
                return CommandResponse<Volume>.Ok(volumes?.FirstOrDefault() ?? new Volume());
            }
            catch (Exception ex)
            {
                return CommandResponse<Volume>.Fail(ex.Message, ErrorCodes.Volume.InspectFailed);
            }
        }

        Task<CommandResponse<VolumePruneResult>> IVolumeDriver.PruneAsync(
            DriverContext context,
            CancellationToken cancellationToken)
        {
            return PruneVolumeAsync(context, cancellationToken);
        }

        private async Task<CommandResponse<VolumePruneResult>> PruneVolumeAsync(
            DriverContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync("volume prune --force", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<VolumePruneResult>.Fail(
                        result.Error ?? "Volume prune failed",
                        ErrorCodes.Volume.PruneFailed);
                }

                return CommandResponse<VolumePruneResult>.Ok(new VolumePruneResult());
            }
            catch (Exception ex)
            {
                return CommandResponse<VolumePruneResult>.Fail(ex.Message, ErrorCodes.Volume.PruneFailed);
            }
        }

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

        public async Task<CommandResponse<VersionInfo>> GetVersionAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Note: --format without quotes works when not going through shell
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

        // === New ISystemDriver methods ===

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

        Task<CommandResponse<SystemPruneResult>> ISystemDriver.PruneAsync(
            DriverContext context,
            SystemPruneConfig config,
            CancellationToken cancellationToken)
        {
            return SystemPruneAsync(context, config, cancellationToken);
        }

        private async Task<CommandResponse<SystemPruneResult>> SystemPruneAsync(
            DriverContext context,
            SystemPruneConfig config,
            CancellationToken cancellationToken)
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

        // ========== IComposeDriver Implementation ==========

        public async Task<CommandResponse<ComposeUpResult>> UpAsync(
            DriverContext context,
            ComposeUpConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "compose" };

                foreach (var file in config.ComposeFiles)
                    args.Add($"-f {file}");

                if (!string.IsNullOrEmpty(config.ProjectName))
                    args.Add($"-p {config.ProjectName}");

                args.Add("up");

                if (config.Detached)
                    args.Add("-d");

                if (config.Build)
                    args.Add("--build");

                if (config.ForceRecreate)
                    args.Add("--force-recreate");

                if (config.RemoveOrphans)
                    args.Add("--remove-orphans");

                if (config.NoDeps)
                    args.Add("--no-deps");

                if (config.Timeout.HasValue)
                    args.Add($"--timeout {config.Timeout.Value}");

                if (config.Services.Count > 0)
                    args.AddRange(config.Services);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ComposeUpResult>.Fail(
                        result.Error ?? "Compose up failed",
                        ErrorCodes.Compose.UpFailed,
                        CreateErrorContext(context, "ComposeUp", result),
                        result.ExitCode);
                }

                return CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult
                {
                    ProjectName = config.ProjectName ?? "default",
                    Services = config.Services.Select(s => s).ToList()
                });
            }
            catch (Exception ex)
            {
                return CommandResponse<ComposeUpResult>.Fail(ex.Message, ErrorCodes.Compose.UpFailed);
            }
        }

        public async Task<CommandResponse<Unit>> DownAsync(
            DriverContext context,
            ComposeDownConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "compose" };

                foreach (var file in config.ComposeFiles)
                    args.Add($"-f {file}");

                if (!string.IsNullOrEmpty(config.ProjectName))
                    args.Add($"-p {config.ProjectName}");

                args.Add("down");

                if (config.RemoveVolumes)
                    args.Add("--volumes");

                if (!string.IsNullOrEmpty(config.RemoveImages))
                    args.Add($"--rmi {config.RemoveImages}");

                if (config.Timeout.HasValue)
                    args.Add($"--timeout {config.Timeout.Value}");

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Compose down failed",
                        ErrorCodes.Compose.DownFailed,
                        CreateErrorContext(context, "ComposeDown", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.DownFailed);
            }
        }

        // Old string-based Compose methods removed - using new config-based methods

        // === New IComposeDriver methods ===

        public async Task<CommandResponse<Unit>> StartAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " start";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose start failed", ErrorCodes.Compose.StartFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.StartFailed);
            }
        }

        public async Task<CommandResponse<Unit>> StopAsync(
            DriverContext context,
            ComposeStopConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " stop";
                if (config.Timeout.HasValue)
                    args += $" --timeout {config.Timeout.Value}";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose stop failed", ErrorCodes.Compose.StopFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.StopFailed);
            }
        }

        public async Task<CommandResponse<Unit>> RestartAsync(
            DriverContext context,
            ComposeRestartConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " restart";
                if (config.Timeout.HasValue)
                    args += $" --timeout {config.Timeout.Value}";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose restart failed", ErrorCodes.Compose.RestartFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.RestartFailed);
            }
        }

        public async Task<CommandResponse<Unit>> PauseAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " pause";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose pause failed", ErrorCodes.Compose.PauseFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.PauseFailed);
            }
        }

        public async Task<CommandResponse<Unit>> UnpauseAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " unpause";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose unpause failed", ErrorCodes.Compose.UnpauseFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.UnpauseFailed);
            }
        }

        public async Task<CommandResponse<Unit>> KillAsync(
            DriverContext context,
            ComposeKillConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += $" kill -s {config.Signal ?? "SIGKILL"}";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose kill failed", ErrorCodes.Compose.KillFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.KillFailed);
            }
        }

        public async Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            ComposeRemoveConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " rm -f";
                if (config.Stop)
                    args += " -s";
                if (config.Volumes)
                    args += " -v";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose rm failed", ErrorCodes.Compose.RemoveFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.RemoveFailed);
            }
        }

        public async Task<CommandResponse<IList<ComposeServiceInfo>>> ListAsync(
            DriverContext context,
            ComposeListConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " ps --format json";
                if (config.All)
                    args += " -a";
                if (config.Quiet)
                    args += " -q";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<ComposeServiceInfo>>.Fail(
                        result.Error ?? "Compose ps failed", ErrorCodes.Compose.ListFailed);
                }

                var services = new List<ComposeServiceInfo>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var service = JsonConvert.DeserializeObject<ComposeServiceInfo>(line);
                        if (service != null) services.Add(service);
                    }
                    catch { }
                }

                return CommandResponse<IList<ComposeServiceInfo>>.Ok(services);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ComposeServiceInfo>>.Fail(ex.Message, ErrorCodes.Compose.ListFailed);
            }
        }

        public async Task<CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            ComposeLogsConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " logs";
                if (config.Follow)
                    args += " -f";
                if (config.Timestamps)
                    args += " -t";
                if (config.Tail.HasValue)
                    args += $" --tail {config.Tail.Value}";
                if (!string.IsNullOrEmpty(config.Since))
                    args += $" --since {config.Since}";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output)
                    : CommandResponse<string>.Fail(result.Error ?? "Compose logs failed", ErrorCodes.Compose.LogsFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.LogsFailed);
            }
        }

        public async Task<CommandResponse<IList<ComposeProcesses>>> TopAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " top";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<IList<ComposeProcesses>>.Ok(new List<ComposeProcesses>())
                    : CommandResponse<IList<ComposeProcesses>>.Fail(result.Error ?? "Compose top failed", ErrorCodes.Compose.TopFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ComposeProcesses>>.Fail(ex.Message, ErrorCodes.Compose.TopFailed);
            }
        }

        public async Task<CommandResponse<string>> ConfigAsync(
            DriverContext context,
            ComposeConfigConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " config";
                if (config.ShowServices)
                    args += " --services";
                if (config.ShowVolumes)
                    args += " --volumes";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output)
                    : CommandResponse<string>.Fail(result.Error ?? "Compose config failed", ErrorCodes.Compose.ConfigFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.ConfigFailed);
            }
        }

        public async Task<CommandResponse<IList<ComposeImage>>> ImagesAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " images";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<IList<ComposeImage>>.Ok(new List<ComposeImage>())
                    : CommandResponse<IList<ComposeImage>>.Fail(result.Error ?? "Compose images failed", ErrorCodes.Compose.ImagesFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ComposeImage>>.Fail(ex.Message, ErrorCodes.Compose.ImagesFailed);
            }
        }

        public async Task<CommandResponse<string>> PortAsync(
            DriverContext context,
            ComposePortConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += $" port {config.Service} {config.PrivatePort}/{config.Protocol}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output.Trim())
                    : CommandResponse<string>.Fail(result.Error ?? "Compose port failed", ErrorCodes.Compose.PortFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.PortFailed);
            }
        }

        public async Task<CommandResponse<Unit>> BuildAsync(
            DriverContext context,
            ComposeBuildConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " build";
                if (config.NoCache)
                    args += " --no-cache";
                if (config.Pull)
                    args += " --pull";
                if (config.ForceRm)
                    args += " --force-rm";
                if (config.Parallel)
                    args += " --parallel";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose build failed", ErrorCodes.Compose.BuildFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.BuildFailed);
            }
        }

        public async Task<CommandResponse<Unit>> PullAsync(
            DriverContext context,
            ComposePullConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " pull";
                if (config.Quiet)
                    args += " -q";
                if (config.IgnorePullFailures)
                    args += " --ignore-pull-failures";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose pull failed", ErrorCodes.Compose.PullFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.PullFailed);
            }
        }

        public async Task<CommandResponse<Unit>> PushAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " push";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose push failed", ErrorCodes.Compose.PushFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.PushFailed);
            }
        }

        public async Task<CommandResponse<string>> ExecuteAsync(
            DriverContext context,
            ComposeExecConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " exec";
                if (config.Detach)
                    args += " -d";
                if (!config.Tty)
                    args += " -T";
                if (config.Privileged)
                    args += " --privileged";
                if (!string.IsNullOrEmpty(config.User))
                    args += $" -u {config.User}";
                if (!string.IsNullOrEmpty(config.WorkDir))
                    args += $" -w {config.WorkDir}";
                if (config.Index.HasValue)
                    args += $" --index {config.Index.Value}";
                args += $" {config.Service} {string.Join(" ", config.Command ?? Array.Empty<string>())}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output)
                    : CommandResponse<string>.Fail(result.Error ?? "Compose exec failed", ErrorCodes.Compose.ExecFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.ExecFailed);
            }
        }

        public async Task<CommandResponse<string>> RunAsync(
            DriverContext context,
            ComposeRunConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " run";
                if (config.Detach)
                    args += " -d";
                if (config.Rm)
                    args += " --rm";
                if (config.NoDeps)
                    args += " --no-deps";
                if (!string.IsNullOrEmpty(config.Name))
                    args += $" --name {config.Name}";
                if (!string.IsNullOrEmpty(config.User))
                    args += $" -u {config.User}";
                args += $" {config.Service}";
                if (config.Command != null && config.Command.Length > 0)
                    args += " " + string.Join(" ", config.Command);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output)
                    : CommandResponse<string>.Fail(result.Error ?? "Compose run failed", ErrorCodes.Compose.RunFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.RunFailed);
            }
        }

        public async Task<CommandResponse<Unit>> ScaleAsync(
            DriverContext context,
            ComposeScaleConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " up -d --scale";
                foreach (var scale in config.Scale)
                    args += $" {scale.Key}={scale.Value}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose scale failed", ErrorCodes.Compose.ScaleFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.ScaleFailed);
            }
        }

        public async Task<CommandResponse<Unit>> CopyAsync(
            DriverContext context,
            ComposeCopyConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += $" cp";
                if (config.Archive)
                    args += " -a";
                if (config.FollowLinks)
                    args += " -L";
                if (config.Index.HasValue)
                    args += $" --index {config.Index.Value}";
                args += $" {config.Source} {config.Destination}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose cp failed", ErrorCodes.Compose.CopyFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.CopyFailed);
            }
        }

        public async Task<CommandResponse<Unit>> CreateAsync(
            DriverContext context,
            ComposeCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = BuildComposeArgs(config);
                args += " create";
                if (config.Build)
                    args += " --build";
                if (config.ForceRecreate)
                    args += " --force-recreate";
                if (config.NoRecreate)
                    args += " --no-recreate";
                if (config.NoBuild)
                    args += " --no-build";
                if (config.Services.Count > 0)
                    args += " " + string.Join(" ", config.Services);

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Compose create failed", ErrorCodes.Compose.CreateFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.CreateFailed);
            }
        }

        private string BuildComposeArgs(ComposeFileConfig config)
        {
            var args = "compose";
            foreach (var file in config.ComposeFiles)
                args += $" -f {file}";
            if (!string.IsNullOrEmpty(config.ProjectName))
                args += $" -p {config.ProjectName}";
            if (!string.IsNullOrEmpty(config.ProjectDirectory))
                args += $" --project-directory {config.ProjectDirectory}";
            return args;
        }

        // ========== IAuthDriver Implementation ==========

        public async Task<CommandResponse<Unit>> LoginAsync(
            DriverContext context,
            RegistryLoginConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "login";
                if (!string.IsNullOrEmpty(config.Username))
                    args += $" -u {config.Username}";
                if (!string.IsNullOrEmpty(config.Password) && !config.PasswordStdin)
                    args += $" -p {config.Password}";
                if (config.PasswordStdin)
                    args += " --password-stdin";
                if (!string.IsNullOrEmpty(config.Server))
                    args += $" {config.Server}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Login failed", ErrorCodes.Auth.LoginFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Auth.LoginFailed);
            }
        }

        public async Task<CommandResponse<Unit>> LogoutAsync(
            DriverContext context,
            string server = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "logout";
                if (!string.IsNullOrEmpty(server))
                    args += $" {server}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Logout failed", ErrorCodes.Auth.LogoutFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Auth.LogoutFailed);
            }
        }

        // ========== IStreamDriver Implementation ==========

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
                    evt.RawJson = line;
                }
                catch { }

                if (evt != null)
                    yield return evt;
            }
        }

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
                    stats.RawJson = line;
                }
                catch { }

                if (stats != null)
                    yield return stats;
            }
        }

        public async Task<CommandResponse<AttachResult>> AttachAsync(
            DriverContext context,
            string containerId,
            AttachConfig config = null,
            CancellationToken cancellationToken = default)
        {
            // Attach requires interactive process handling which is complex
            // Return a basic implementation that can be enhanced later
            return await Task.FromResult(CommandResponse<AttachResult>.Fail(
                "Attach is not yet fully implemented in the CLI driver",
                ErrorCodes.Container.AttachFailed));
        }

        // ========== IStackDriver Implementation ==========

        public async Task<CommandResponse<IList<StackInfo>>> ListAsync(
            DriverContext context,
            StackListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "stack ls --format \"{{json .}}\"";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<StackInfo>>.Fail(
                        result.Error ?? "Stack list failed", ErrorCodes.Stack.ListFailed);
                }

                var stacks = new List<StackInfo>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var stack = JsonConvert.DeserializeObject<StackInfo>(line);
                        if (stack != null) stacks.Add(stack);
                    }
                    catch { }
                }

                return CommandResponse<IList<StackInfo>>.Ok(stacks);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<StackInfo>>.Fail(ex.Message, ErrorCodes.Stack.ListFailed);
            }
        }

        public async Task<CommandResponse<IList<StackTask>>> GetTasksAsync(
            DriverContext context,
            string stackName,
            StackTaskFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"stack ps --format '{{{{json .}}}}' {stackName}";
                if (filter?.NoTrunc == true)
                    args = args.Replace("stack ps", "stack ps --no-trunc");

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<StackTask>>.Fail(
                        result.Error ?? "Stack ps failed", ErrorCodes.Stack.TasksFailed);
                }

                var tasks = new List<StackTask>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var task = JsonConvert.DeserializeObject<StackTask>(line);
                        if (task != null) tasks.Add(task);
                    }
                    catch { }
                }

                return CommandResponse<IList<StackTask>>.Ok(tasks);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<StackTask>>.Fail(ex.Message, ErrorCodes.Stack.TasksFailed);
            }
        }

        public async Task<CommandResponse<StackDeployResult>> DeployAsync(
            DriverContext context,
            StackDeployConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "stack deploy";
                foreach (var file in config.ComposeFiles)
                    args += $" -c {file}";
                if (config.Prune)
                    args += " --prune";
                if (config.WithRegistryAuth)
                    args += " --with-registry-auth";
                args += $" {config.StackName}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<StackDeployResult>.Fail(
                        result.Error ?? "Stack deploy failed", ErrorCodes.Stack.DeployFailed);
                }

                return CommandResponse<StackDeployResult>.Ok(new StackDeployResult { StackName = config.StackName });
            }
            catch (Exception ex)
            {
                return CommandResponse<StackDeployResult>.Fail(ex.Message, ErrorCodes.Stack.DeployFailed);
            }
        }

        Task<CommandResponse<Unit>> IStackDriver.RemoveAsync(
            DriverContext context,
            string[] stackNames,
            CancellationToken cancellationToken)
        {
            return StackRemoveAsync(context, stackNames, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> StackRemoveAsync(
            DriverContext context,
            string[] stackNames,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = $"stack rm {string.Join(" ", stackNames)}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Stack rm failed", ErrorCodes.Stack.RemoveFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Stack.RemoveFailed);
            }
        }

        public async Task<CommandResponse<IList<StackServiceInfo>>> GetServicesAsync(
            DriverContext context,
            string stackName,
            StackServiceFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"stack services --format '{{{{json .}}}}' {stackName}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<StackServiceInfo>>.Fail(
                        result.Error ?? "Stack services failed", ErrorCodes.Stack.ServicesFailed);
                }

                var services = new List<StackServiceInfo>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var svc = JsonConvert.DeserializeObject<StackServiceInfo>(line);
                        if (svc != null) services.Add(svc);
                    }
                    catch { }
                }

                return CommandResponse<IList<StackServiceInfo>>.Ok(services);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<StackServiceInfo>>.Fail(ex.Message, ErrorCodes.Stack.ServicesFailed);
            }
        }

        // ========== IServiceDriver Implementation ==========

        public async Task<CommandResponse<ServiceCreateResult>> CreateAsync(
            DriverContext context,
            ServiceCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "service", "create" };

                if (!string.IsNullOrEmpty(config.Name))
                    args.Add($"--name {config.Name}");
                if (config.Replicas.HasValue)
                    args.Add($"--replicas {config.Replicas.Value}");
                if (!string.IsNullOrEmpty(config.Mode))
                    args.Add($"--mode {config.Mode}");
                foreach (var env in config.Environment)
                    args.Add($"-e {env.Key}={env.Value}");
                foreach (var label in config.Labels)
                    args.Add($"--label {label.Key}={label.Value}");
                foreach (var port in config.Ports)
                    args.Add($"-p {port.PublishedPort}:{port.TargetPort}/{port.Protocol}");
                foreach (var network in config.Networks)
                    args.Add($"--network {network}");
                if (config.Detach)
                    args.Add("-d");
                if (config.Quiet)
                    args.Add("-q");

                args.Add(config.Image);
                if (config.Command != null)
                    args.AddRange(config.Command);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ServiceCreateResult>.Fail(
                        result.Error ?? "Service create failed", ErrorCodes.Service.CreateFailed);
                }

                return CommandResponse<ServiceCreateResult>.Ok(new ServiceCreateResult { Id = result.Output.Trim() });
            }
            catch (Exception ex)
            {
                return CommandResponse<ServiceCreateResult>.Fail(ex.Message, ErrorCodes.Service.CreateFailed);
            }
        }

        Task<CommandResponse<Unit>> IServiceDriver.RemoveAsync(
            DriverContext context,
            string[] serviceIds,
            CancellationToken cancellationToken)
        {
            return ServiceRemoveAsync(context, serviceIds, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> ServiceRemoveAsync(
            DriverContext context,
            string[] serviceIds,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync($"service rm {string.Join(" ", serviceIds)}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Service rm failed", ErrorCodes.Service.RemoveFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.RemoveFailed);
            }
        }

        public async Task<CommandResponse<Unit>> UpdateAsync(
            DriverContext context,
            string serviceId,
            ServiceUpdateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "service", "update" };

                if (!string.IsNullOrEmpty(config.Image))
                    args.Add($"--image {config.Image}");
                if (config.Replicas.HasValue)
                    args.Add($"--replicas {config.Replicas.Value}");
                foreach (var env in config.EnvAdd)
                    args.Add($"--env-add {env.Key}={env.Value}");
                foreach (var env in config.EnvRm)
                    args.Add($"--env-rm {env}");
                if (config.Force)
                    args.Add("--force");
                if (config.Detach)
                    args.Add("-d");

                args.Add(serviceId);

                var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Service update failed", ErrorCodes.Service.UpdateFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.UpdateFailed);
            }
        }

        public async Task<CommandResponse<Unit>> RollbackAsync(
            DriverContext context,
            string serviceId,
            bool detach = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"service rollback";
                if (detach)
                    args += " -d";
                args += $" {serviceId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Service rollback failed", ErrorCodes.Service.RollbackFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.RollbackFailed);
            }
        }

        Task<CommandResponse<IList<ServiceInfo>>> IServiceDriver.ListAsync(
            DriverContext context,
            ServiceListFilter filter,
            CancellationToken cancellationToken)
        {
            return ServiceListAsync(context, filter, cancellationToken);
        }

        private async Task<CommandResponse<IList<ServiceInfo>>> ServiceListAsync(
            DriverContext context,
            ServiceListFilter filter,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = "service ls --format \"{{json .}}\"";
                if (filter?.Quiet == true)
                    args = "service ls -q";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<ServiceInfo>>.Fail(
                        result.Error ?? "Service list failed", ErrorCodes.Service.ListFailed);
                }

                var services = new List<ServiceInfo>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var svc = JsonConvert.DeserializeObject<ServiceInfo>(line);
                        if (svc != null) services.Add(svc);
                    }
                    catch { }
                }

                return CommandResponse<IList<ServiceInfo>>.Ok(services);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ServiceInfo>>.Fail(ex.Message, ErrorCodes.Service.ListFailed);
            }
        }

        public async Task<CommandResponse<ServiceDetails>> InspectAsync(
            DriverContext context,
            string serviceId,
            bool pretty = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"service inspect";
                if (pretty)
                    args += " --pretty";
                args += $" {serviceId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<ServiceDetails>.Fail(
                        result.Error ?? "Service inspect failed", ErrorCodes.Service.InspectFailed);
                }

                var details = JsonConvert.DeserializeObject<List<ServiceDetails>>(result.Output)?.FirstOrDefault();
                return details != null
                    ? CommandResponse<ServiceDetails>.Ok(details)
                    : CommandResponse<ServiceDetails>.Fail("Service not found", ErrorCodes.Service.NotFound);
            }
            catch (Exception ex)
            {
                return CommandResponse<ServiceDetails>.Fail(ex.Message, ErrorCodes.Service.InspectFailed);
            }
        }

        public async Task<CommandResponse<IList<ServiceTask>>> GetTasksAsync(
            DriverContext context,
            string serviceId,
            ServiceTaskFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"service ps --format '{{{{json .}}}}' {serviceId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<ServiceTask>>.Fail(
                        result.Error ?? "Service ps failed", ErrorCodes.Service.TasksFailed);
                }

                var tasks = new List<ServiceTask>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        var task = JsonConvert.DeserializeObject<ServiceTask>(line);
                        if (task != null) tasks.Add(task);
                    }
                    catch { }
                }

                return CommandResponse<IList<ServiceTask>>.Ok(tasks);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ServiceTask>>.Fail(ex.Message, ErrorCodes.Service.TasksFailed);
            }
        }

        Task<CommandResponse<string>> IServiceDriver.GetLogsAsync(
            DriverContext context,
            string serviceId,
            ServiceLogsConfig config,
            CancellationToken cancellationToken)
        {
            return ServiceGetLogsAsync(context, serviceId, config, cancellationToken);
        }

        private async Task<CommandResponse<string>> ServiceGetLogsAsync(
            DriverContext context,
            string serviceId,
            ServiceLogsConfig config,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = "service logs";
                if (config?.Follow == true)
                    args += " -f";
                if (config?.Timestamps == true)
                    args += " -t";
                if (config?.Tail.HasValue == true)
                    args += $" --tail {config.Tail.Value}";
                args += $" {serviceId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<string>.Ok(result.Output)
                    : CommandResponse<string>.Fail(result.Error ?? "Service logs failed", ErrorCodes.Service.LogsFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Service.LogsFailed);
            }
        }

        public async Task<CommandResponse<Unit>> ScaleAsync(
            DriverContext context,
            Dictionary<string, int> serviceReplicas,
            bool detach = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scaleArgs = string.Join(" ", serviceReplicas.Select(sr => $"{sr.Key}={sr.Value}"));
                var args = $"service scale {scaleArgs}";
                if (detach)
                    args = args.Replace("service scale", "service scale -d");

                var result = await ExecuteCommandAsync(args, cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(result.Error ?? "Service scale failed", ErrorCodes.Service.ScaleFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.ScaleFailed);
            }
        }

        // ========== IMachineDriver Implementation ==========

        public bool IsAvailable()
        {
            return CommandExtensions.IsMachineBinaryPresent();
        }

        Task<CommandResponse<IList<MachineInfo>>> IMachineDriver.ListAsync(CancellationToken cancellationToken)
        {
            return MachineListAsync(cancellationToken);
        }

        private async Task<CommandResponse<IList<MachineInfo>>> MachineListAsync(CancellationToken cancellationToken)
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
                    catch { }
                }

                return CommandResponse<IList<MachineInfo>>.Ok(machines);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<MachineInfo>>.Fail(ex.Message, ErrorCodes.Machine.ListFailed);
            }
        }

        Task<CommandResponse<MachineDetails>> IMachineDriver.InspectAsync(string machineName, CancellationToken cancellationToken)
        {
            return MachineInspectAsync(machineName, cancellationToken);
        }

        private async Task<CommandResponse<MachineDetails>> MachineInspectAsync(string machineName, CancellationToken cancellationToken)
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

        Task<CommandResponse<Unit>> IMachineDriver.StartAsync(string machineName, CancellationToken cancellationToken)
        {
            return MachineStartAsync(machineName, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> MachineStartAsync(string machineName, CancellationToken cancellationToken)
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

        Task<CommandResponse<Unit>> IMachineDriver.StopAsync(string machineName, CancellationToken cancellationToken)
        {
            return MachineStopAsync(machineName, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> MachineStopAsync(string machineName, CancellationToken cancellationToken)
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

        public async Task<CommandResponse<Unit>> RestartAsync(string machineName, CancellationToken cancellationToken = default)
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

        // Helper methods

        private async Task<SimpleCommandResult> ExecuteDockerCliCommandAsync(string arguments, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "dockercli".ResolveBinary(),
                            Arguments = arguments,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    var output = new System.Text.StringBuilder();
                    var error = new System.Text.StringBuilder();

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

        private async Task<SimpleCommandResult> ExecuteMachineCommandAsync(string arguments, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "docker-machine".ResolveBinary(),
                            Arguments = arguments,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    var output = new System.Text.StringBuilder();
                    var error = new System.Text.StringBuilder();

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

        private async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = DockerCommand,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var reader = process.StandardOutput;

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                    break;

                yield return line;
            }

            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch { }
            }
        }

        private async Task<SimpleCommandResult> ExecuteCommandAsync(string arguments, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = DockerCommand,
                            Arguments = arguments,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    var output = new System.Text.StringBuilder();
                    var error = new System.Text.StringBuilder();

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

        private ErrorContext CreateErrorContext(DriverContext context, string operation, SimpleCommandResult result)
        {
            return new ErrorContext(operation)
            {
                DriverId = context.DriverId,
                Host = context.Host,
                ExitCode = result.ExitCode,
                StdOut = result.Output,
                StdErr = result.Error
            };
        }

        private class SimpleCommandResult
        {
            public bool Success { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
            public int ExitCode { get; set; }
        }
    }
}
