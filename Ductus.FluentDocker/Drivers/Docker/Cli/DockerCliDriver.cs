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
    public class DockerCliDriver : IDriver, IContainerDriver, IImageDriver, INetworkDriver, IVolumeDriver, ISystemDriver, IComposeDriver
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

        Task<CommandResponse<Container>> IContainerDriver.InspectAsync(
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
                foreach (var line in lines.Reverse())
                {
                    if (line.Contains("sha256:"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        imageId = parts.LastOrDefault(p => p.StartsWith("sha256:")) ?? "";
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
                var result = await ExecuteCommandAsync("network ls --format '{{json .}}'", cancellationToken);

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
                var result = await ExecuteCommandAsync("volume ls --format '{{json .}}'", cancellationToken);

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

        Task<CommandResponse<Unit>> IComposeDriver.StartAsync(
            DriverContext context,
            string composeFile,
            CancellationToken cancellationToken)
        {
            return ComposeStartAsync(context, composeFile, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> ComposeStartAsync(
            DriverContext context,
            string composeFile,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteCommandAsync($"compose -f {composeFile} start", cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Compose start failed",
                        ErrorCodes.Compose.StartFailed,
                        CreateErrorContext(context, "ComposeStart", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.StartFailed);
            }
        }

        Task<CommandResponse<Unit>> IComposeDriver.StopAsync(
            DriverContext context,
            string composeFile,
            int? timeout,
            CancellationToken cancellationToken)
        {
            return ComposeStopAsync(context, composeFile, timeout, cancellationToken);
        }

        private async Task<CommandResponse<Unit>> ComposeStopAsync(
            DriverContext context,
            string composeFile,
            int? timeout,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = $"compose -f {composeFile} stop";
                if (timeout.HasValue)
                    args += $" --timeout {timeout.Value}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<Unit>.Fail(
                        result.Error ?? "Compose stop failed",
                        ErrorCodes.Compose.StopFailed,
                        CreateErrorContext(context, "ComposeStop", result),
                        result.ExitCode);
                }

                return CommandResponse<Unit>.Ok(Unit.Default);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Compose.StopFailed);
            }
        }

        Task<CommandResponse<IList<ComposeService>>> IComposeDriver.ListAsync(
            DriverContext context,
            string composeFile,
            string projectName,
            CancellationToken cancellationToken)
        {
            return ComposeListAsync(context, composeFile, projectName, cancellationToken);
        }

        private async Task<CommandResponse<IList<ComposeService>>> ComposeListAsync(
            DriverContext context,
            string composeFile,
            string projectName,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = $"compose -f {composeFile}";
                if (!string.IsNullOrEmpty(projectName))
                    args += $" -p {projectName}";
                args += " ps --format json";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<IList<ComposeService>>.Fail(
                        result.Error ?? "Compose ps failed",
                        ErrorCodes.Compose.ListFailed,
                        CreateErrorContext(context, "ComposePsDriver", result),
                        result.ExitCode);
                }

                var services = new List<ComposeService>();
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        var service = JsonConvert.DeserializeObject<ComposeService>(line);
                        if (service != null)
                            services.Add(service);
                    }
                    catch { /* Skip malformed lines */ }
                }

                return CommandResponse<IList<ComposeService>>.Ok(services);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ComposeService>>.Fail(ex.Message, ErrorCodes.Compose.ListFailed);
            }
        }

        Task<CommandResponse<string>> IComposeDriver.GetLogsAsync(
            DriverContext context,
            string composeFile,
            bool follow,
            CancellationToken cancellationToken)
        {
            return ComposeGetLogsAsync(context, composeFile, follow, cancellationToken);
        }

        private async Task<CommandResponse<string>> ComposeGetLogsAsync(
            DriverContext context,
            string composeFile,
            bool follow,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = $"compose -f {composeFile} logs";
                if (follow)
                    args += " --follow";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<string>.Fail(
                        result.Error ?? "Compose logs failed",
                        ErrorCodes.Compose.LogsFailed,
                        CreateErrorContext(context, "ComposeLogs", result),
                        result.ExitCode);
                }

                return CommandResponse<string>.Ok(result.Output);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.LogsFailed);
            }
        }

        public async Task<CommandResponse<string>> ExecuteAsync(
            DriverContext context,
            string composeFile,
            string service,
            string[] command,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = $"compose -f {composeFile} exec {service} {string.Join(" ", command)}";

                var result = await ExecuteCommandAsync(args, cancellationToken);

                if (!result.Success)
                {
                    return CommandResponse<string>.Fail(
                        result.Error ?? "Compose exec failed",
                        ErrorCodes.Compose.ExecFailed,
                        CreateErrorContext(context, "ComposeExec", result),
                        result.ExitCode);
                }

                return CommandResponse<string>.Ok(result.Output);
            }
            catch (Exception ex)
            {
                return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Compose.ExecFailed);
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
