using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Drivers;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Model.Networks;
using Ductus.FluentDocker.Model.Volumes;
using Newtonsoft.Json;
using Container = Ductus.FluentDocker.Model.Containers.Container;

namespace Ductus.FluentDocker.Drivers.Docker.Cli
{
    /// <summary>
    /// Docker CLI driver implementation for v3.0.0.
    /// </summary>
    public class DockerCliDriver : IDriver, IContainerDriver, IImageDriver, INetworkDriver, IVolumeDriver, ISystemDriver
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
                var result = await ExecuteCommandAsync("version --format '{{.Server.Version}}'", cancellationToken);
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
        public async Task<CommandResponse<ContainerCreateResult>> CreateAsync(
            DriverContext context,
            ContainerCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = new List<string> { "create" };

                if (!string.IsNullOrEmpty(config.Name))
                    args.Add($"--name {config.Name}");

                foreach (var env in config.Environment)
                    args.Add($"-e {env.Key}={env.Value}");

                foreach (var port in config.PortBindings)
                    args.Add($"-p {port.Value}:{port.Key}");

                foreach (var volume in config.Volumes)
                    args.Add($"-v {volume}");

                if (!string.IsNullOrEmpty(config.NetworkMode))
                    args.Add($"--network {config.NetworkMode}");

                foreach (var label in config.Labels)
                    args.Add($"--label {label.Key}={label.Value}");

                args.Add(config.Image);

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

        public async Task<CommandResponse<Unit>> StopAsync(
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

        Task<CommandResponse<Unit>> IContainerDriver.RemoveAsync(
            DriverContext context,
            string containerId,
            bool force,
            CancellationToken cancellationToken)
        {
            return RemoveContainerAsync(context, containerId, force, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> RemoveContainerAsync(
            DriverContext context,
            string containerId,
            bool force,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = $"rm";
                if (force)
                    args += " -f";
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

        public async Task<CommandResponse<IList<Container>>> ListAsync(
            DriverContext context,
            ContainerListFilter filter = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "ps --format '{{json .}}'";

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

        public async Task<CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            string containerId,
            bool follow = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"logs";
                if (follow)
                    args += " -f";
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

        Task<CommandResponse<Unit>> IImageDriver.RemoveAsync(
            DriverContext context,
            string imageId,
            bool force,
            CancellationToken cancellationToken)
        {
            return RemoveImageAsync(context, imageId, force, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> RemoveImageAsync(
            DriverContext context,
            string imageId,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"rmi";
                if (force)
                    args += " -f";
                args += $" {imageId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Image remove failed",
                        ErrorCodes.Image.RemoveFailed);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.RemoveFailed);
            }
        }

        public Task<CommandResponse<ImageBuildResult>> BuildAsync(
            DriverContext context,
            ImageBuildConfig config,
            IProgress<ImageBuildProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            // Build implementation would be more complex, stub for now
            return Task.FromResult(CommandResponse<ImageBuildResult>.Fail(
                "Image build not yet implemented",
                ErrorCodes.Image.BuildFailed));
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
                var args = "images --format '{{json .}}'";

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

        // INetworkDriver, IVolumeDriver, ISystemDriver - Simplified implementations
        public Task<CommandResponse<NetworkCreateResult>> CreateAsync(
            DriverContext context,
            NetworkCreateConfig config,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<NetworkCreateResult>.Fail(
                "Network operations not yet implemented",
                ErrorCodes.Network.CreateFailed));
        }

        Task<CommandResponse<Unit>> INetworkDriver.RemoveAsync(DriverContext context, string networkId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Network operations not yet implemented",
                ErrorCodes.Network.RemoveFailed));
        }

        Task<CommandResponse<IList<Network>>> INetworkDriver.ListAsync(DriverContext context, NetworkListFilter filter, CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandResponse<IList<Network>>.Fail(
                "Network operations not yet implemented",
                ErrorCodes.General.Unknown));
        }

        public Task<CommandResponse<Unit>> ConnectAsync(DriverContext context, string networkId, string containerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Network operations not yet implemented",
                ErrorCodes.Network.ConnectFailed));
        }

        public Task<CommandResponse<Unit>> DisconnectAsync(DriverContext context, string networkId, string containerId, bool force = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Network operations not yet implemented",
                ErrorCodes.Network.DisconnectFailed));
        }

        public Task<CommandResponse<VolumeCreateResult>> CreateAsync(DriverContext context, VolumeCreateConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CommandResponse<VolumeCreateResult>.Fail(
                "Volume operations not yet implemented",
                ErrorCodes.Volume.CreateFailed));
        }

        Task<CommandResponse<Unit>> IVolumeDriver.RemoveAsync(DriverContext context, string volumeName, bool force, CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandResponse<Unit>.Fail(
                "Volume operations not yet implemented",
                ErrorCodes.Volume.RemoveFailed));
        }

        Task<CommandResponse<IList<Volume>>> IVolumeDriver.ListAsync(DriverContext context, VolumeListFilter filter, CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandResponse<IList<Volume>>.Fail(
                "Volume operations not yet implemented",
                ErrorCodes.General.Unknown));
        }

        Task<CommandResponse<Volume>> IVolumeDriver.InspectAsync(DriverContext context, string volumeName, CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandResponse<Volume>.Fail(
                "Volume operations not yet implemented",
                ErrorCodes.Volume.InspectFailed));
        }

        public async Task<CommandResponse<SystemInfo>> GetInfoAsync(
            DriverContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("info --format '{{json .}}'", cancellationToken);

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
                var result = await ExecuteCommandAsync("version --format '{{json .}}'", cancellationToken);

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

        // Helper methods
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
