using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Model.Drivers;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Model.Volumes;
using Container = Ductus.FluentDocker.Model.Containers.Container;
using ContainerState = Ductus.FluentDocker.Model.Containers.ContainerState;

namespace Ductus.FluentDocker.Tests.V3.Mock
{
    /// <summary>
    /// Comprehensive mock driver for testing - implements all driver interfaces.
    /// </summary>
    public class MockDriver : IDriver, IContainerDriver, IImageDriver, INetworkDriver, IVolumeDriver, ISystemDriver, IComposeDriver
    {
        private readonly DriverType _driverType;
        private readonly RuntimeType _runtimeType;
        private readonly Dictionary<string, Container> _containers = new Dictionary<string, Container>();
        private readonly Dictionary<string, Image> _images = new Dictionary<string, Image>();
        private readonly Dictionary<string, Network> _networks = new Dictionary<string, Network>();
        private readonly Dictionary<string, Volume> _volumes = new Dictionary<string, Volume>();
        private readonly List<MethodCall> _methodCalls = new List<MethodCall>();

        public bool SimulateFailure { get; set; }
        public string FailureMessage { get; set; } = "Simulated failure";
        public string FailureErrorCode { get; set; } = ErrorCodes.General.Unknown;

        public MockDriver(DriverType driverType = DriverType.DockerCli, RuntimeType runtimeType = RuntimeType.Docker)
        {
            _driverType = driverType;
            _runtimeType = runtimeType;
        }

        public IReadOnlyList<MethodCall> MethodCalls => _methodCalls;

        /// <summary>
        /// Gets the containers stored in this mock driver (for test assertions).
        /// </summary>
        public IReadOnlyDictionary<string, Container> Containers => _containers;

        /// <summary>
        /// Gets the images stored in this mock driver (for test assertions).
        /// </summary>
        public IReadOnlyDictionary<string, Image> Images => _images;

        /// <summary>
        /// Gets the networks stored in this mock driver (for test assertions).
        /// </summary>
        public IReadOnlyDictionary<string, Network> Networks => _networks;

        /// <summary>
        /// Gets the volumes stored in this mock driver (for test assertions).
        /// </summary>
        public IReadOnlyDictionary<string, Volume> Volumes => _volumes;

        // IDriver implementation
        public DriverType Type => _driverType;
        public RuntimeType Runtime => _runtimeType;

        public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(GetCapabilitiesAsync));
            return Task.FromResult(DriverCapabilities.Default());
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(IsHealthyAsync));
            return Task.FromResult(!SimulateFailure);
        }

        public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(InitializeAsync), context);
            return Task.CompletedTask;
        }

        // IContainerDriver implementation
        public Task<CommandResponse<ContainerCreateResult>> CreateAsync(DriverContext context, ContainerCreateConfig config, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(CreateAsync), context, config);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<ContainerCreateResult>.Fail(FailureMessage, FailureErrorCode));

            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            var container = new Container
            {
                Id = id,
                Name = config.Name ?? $"container-{id}",
                Image = config.Image,
                State = new ContainerState { Status = "created" }
            };

            _containers[id] = container;

            return Task.FromResult(CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = id }));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> StartAsync(DriverContext context, string containerId, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(StartAsync), context, containerId);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            if (_containers.TryGetValue(containerId, out var container))
            {
                container.State = new ContainerState { Status = "running" };
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
            }

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail($"Container {containerId} not found", ErrorCodes.Container.NotFound));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> StopAsync(DriverContext context, string containerId, int? timeout = null, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(StopAsync), context, containerId, timeout);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            if (_containers.TryGetValue(containerId, out var container))
            {
                container.State = new ContainerState { Status = "exited" };
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
            }

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail($"Container {containerId} not found", ErrorCodes.Container.NotFound));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> RemoveAsync(DriverContext context, string containerId, bool force = false, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(RemoveAsync), context, containerId, force);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            if (_containers.Remove(containerId))
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail($"Container {containerId} not found", ErrorCodes.Container.NotFound));
        }

        public Task<CommandResponse<Container>> InspectAsync(DriverContext context, string containerId, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(InspectAsync), context, containerId);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Container>.Fail(FailureMessage, FailureErrorCode));

            if (_containers.TryGetValue(containerId, out var container))
                return Task.FromResult(CommandResponse<Container>.Ok(container));

            return Task.FromResult(CommandResponse<Container>.Fail($"Container {containerId} not found", ErrorCodes.Container.NotFound));
        }

        public Task<CommandResponse<IList<Container>>> ListAsync(DriverContext context, ContainerListFilter filter = null, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(ListAsync), context, filter);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<IList<Container>>.Fail(FailureMessage, FailureErrorCode));

            var containers = new List<Container>(_containers.Values);
            return Task.FromResult(CommandResponse<IList<Container>>.Ok(containers));
        }

        public Task<CommandResponse<string>> GetLogsAsync(DriverContext context, string containerId, bool follow = false, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(GetLogsAsync), context, containerId, follow);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<string>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<string>.Ok($"Logs for container {containerId}"));
        }

        // IImageDriver implementation
        public Task<CommandResponse<Model.Drivers.Unit>> PullAsync(DriverContext context, string image, string tag = "latest", IProgress<ImagePullProgress> progress = null, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(PullAsync), context, image, tag);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            _images[id] = new Image { Id = id, Repository = image, Tags = new List<string> { tag } };

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        Task<CommandResponse<Model.Drivers.Unit>> IImageDriver.RemoveAsync(DriverContext context, string imageId, bool force, CancellationToken cancellationToken)
        {
            RecordCall(nameof(IImageDriver.RemoveAsync), context, imageId, force);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            if (_images.Remove(imageId))
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail($"Image {imageId} not found", ErrorCodes.Image.NotFound));
        }

        public Task<CommandResponse<ImageBuildResult>> BuildAsync(DriverContext context, ImageBuildConfig config, IProgress<ImageBuildProgress> progress = null, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(BuildAsync), context, config);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<ImageBuildResult>.Fail(FailureMessage, FailureErrorCode));

            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            return Task.FromResult(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult { ImageId = id }));
        }

        public Task<CommandResponse<IList<Image>>> ListAsync(DriverContext context, ImageListFilter filter, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(ListAsync), context, filter);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<IList<Image>>.Fail(FailureMessage, FailureErrorCode));

            var images = new List<Image>(_images.Values);
            return Task.FromResult(CommandResponse<IList<Image>>.Ok(images));
        }

        Task<CommandResponse<Image>> IImageDriver.InspectAsync(DriverContext context, string imageId, CancellationToken cancellationToken)
        {
            RecordCall(nameof(IImageDriver.InspectAsync), context, imageId);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Image>.Fail(FailureMessage, FailureErrorCode));

            if (_images.TryGetValue(imageId, out var image))
                return Task.FromResult(CommandResponse<Image>.Ok(image));

            return Task.FromResult(CommandResponse<Image>.Fail($"Image {imageId} not found", ErrorCodes.Image.NotFound));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> TagAsync(DriverContext context, string imageId, string repository, string tag, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(TagAsync), context, imageId, repository, tag);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        // INetworkDriver implementation
        public Task<CommandResponse<NetworkCreateResult>> CreateAsync(DriverContext context, NetworkCreateConfig config, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(CreateAsync), context, config);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<NetworkCreateResult>.Fail(FailureMessage, FailureErrorCode));

            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            _networks[id] = new Network { Id = id, Name = config.Name };

            return Task.FromResult(CommandResponse<NetworkCreateResult>.Ok(new NetworkCreateResult { Id = id }));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> RemoveAsync(DriverContext context, string networkId, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(RemoveAsync), context, networkId);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            if (_networks.Remove(networkId))
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail($"Network {networkId} not found", ErrorCodes.Network.NotFound));
        }

        public Task<CommandResponse<IList<Network>>> ListAsync(DriverContext context, NetworkListFilter filter, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(ListAsync), context, filter);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<IList<Network>>.Fail(FailureMessage, FailureErrorCode));

            var networks = new List<Network>(_networks.Values);
            return Task.FromResult(CommandResponse<IList<Network>>.Ok(networks));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> ConnectAsync(DriverContext context, string networkId, string containerId, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(ConnectAsync), context, networkId, containerId);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> DisconnectAsync(DriverContext context, string networkId, string containerId, bool force = false, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(DisconnectAsync), context, networkId, containerId, force);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        Task<CommandResponse<Network>> INetworkDriver.InspectAsync(DriverContext context, string networkId, CancellationToken cancellationToken)
        {
            RecordCall("INetworkDriver.InspectAsync", context, networkId);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Network>.Fail(FailureMessage, FailureErrorCode));

            if (_networks.TryGetValue(networkId, out var network))
                return Task.FromResult(CommandResponse<Network>.Ok(network));

            return Task.FromResult(CommandResponse<Network>.Fail($"Network {networkId} not found", ErrorCodes.Network.NotFound));
        }

        public Task<CommandResponse<NetworkPruneResult>> PruneAsync(DriverContext context, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(PruneAsync), context);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<NetworkPruneResult>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<NetworkPruneResult>.Ok(new NetworkPruneResult()));
        }

        // IVolumeDriver implementation
        public Task<CommandResponse<VolumeCreateResult>> CreateAsync(DriverContext context, VolumeCreateConfig config, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(CreateAsync), context, config);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<VolumeCreateResult>.Fail(FailureMessage, FailureErrorCode));

            var name = config.Name ?? Guid.NewGuid().ToString("N").Substring(0, 12);
            _volumes[name] = new Volume { Name = name };

            return Task.FromResult(CommandResponse<VolumeCreateResult>.Ok(new VolumeCreateResult { Name = name }));
        }

        Task<CommandResponse<Model.Drivers.Unit>> IVolumeDriver.RemoveAsync(DriverContext context, string volumeName, bool force, CancellationToken cancellationToken)
        {
            RecordCall("IVolumeDriver.RemoveAsync", context, volumeName, force);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            if (_volumes.Remove(volumeName))
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail($"Volume {volumeName} not found", ErrorCodes.Volume.NotFound));
        }

        public Task<CommandResponse<IList<Volume>>> ListAsync(DriverContext context, VolumeListFilter filter, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(ListAsync), context, filter);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<IList<Volume>>.Fail(FailureMessage, FailureErrorCode));

            var volumes = new List<Volume>(_volumes.Values);
            return Task.FromResult(CommandResponse<IList<Volume>>.Ok(volumes));
        }

        Task<CommandResponse<Volume>> IVolumeDriver.InspectAsync(DriverContext context, string volumeName, CancellationToken cancellationToken)
        {
            RecordCall("IVolumeDriver.InspectAsync", context, volumeName);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Volume>.Fail(FailureMessage, FailureErrorCode));

            if (_volumes.TryGetValue(volumeName, out var volume))
                return Task.FromResult(CommandResponse<Volume>.Ok(volume));

            return Task.FromResult(CommandResponse<Volume>.Fail($"Volume {volumeName} not found", ErrorCodes.Volume.NotFound));
        }

        Task<CommandResponse<VolumePruneResult>> IVolumeDriver.PruneAsync(DriverContext context, CancellationToken cancellationToken)
        {
            RecordCall(nameof(IVolumeDriver.PruneAsync), context);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<VolumePruneResult>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<VolumePruneResult>.Ok(new VolumePruneResult()));
        }

        // ISystemDriver implementation
        public Task<CommandResponse<SystemInfo>> GetInfoAsync(DriverContext context, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(GetInfoAsync), context);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<SystemInfo>.Fail(FailureMessage, FailureErrorCode));

            var info = new SystemInfo
            {
                ServerVersion = "20.10.0",
                OperatingSystem = "Linux",
                Architecture = "x86_64"
            };

            return Task.FromResult(CommandResponse<SystemInfo>.Ok(info));
        }

        public Task<CommandResponse<VersionInfo>> GetVersionAsync(DriverContext context, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(GetVersionAsync), context);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<VersionInfo>.Fail(FailureMessage, FailureErrorCode));

            var version = new VersionInfo
            {
                Version = "20.10.0",
                ApiVersion = "1.41"
            };

            return Task.FromResult(CommandResponse<VersionInfo>.Ok(version));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> PingAsync(DriverContext context, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(PingAsync), context);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        // IComposeDriver stub implementation
        public Task<CommandResponse<ComposeUpResult>> UpAsync(DriverContext context, ComposeUpConfig config, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(UpAsync), context, config);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<ComposeUpResult>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult()));
        }

        public Task<CommandResponse<Model.Drivers.Unit>> DownAsync(DriverContext context, ComposeDownConfig config, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(DownAsync), context, config);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        Task<CommandResponse<Model.Drivers.Unit>> IComposeDriver.StartAsync(DriverContext context, string composeFile, CancellationToken cancellationToken)
        {
            RecordCall("IComposeDriver.StartAsync", context, composeFile);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        Task<CommandResponse<Model.Drivers.Unit>> IComposeDriver.StopAsync(DriverContext context, string composeFile, int? timeout, CancellationToken cancellationToken)
        {
            RecordCall("IComposeDriver.StopAsync", context, composeFile, timeout);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<Model.Drivers.Unit>.Ok(Model.Drivers.Unit.Default));
        }

        Task<CommandResponse<IList<ComposeService>>> IComposeDriver.ListAsync(DriverContext context, string composeFile, string projectName, CancellationToken cancellationToken)
        {
            RecordCall("IComposeDriver.ListAsync", context, composeFile, projectName);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<IList<ComposeService>>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<IList<ComposeService>>.Ok(new List<ComposeService>()));
        }

        Task<CommandResponse<string>> IComposeDriver.GetLogsAsync(DriverContext context, string composeFile, bool follow, CancellationToken cancellationToken)
        {
            RecordCall("IComposeDriver.GetLogsAsync", context, composeFile, follow);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<string>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<string>.Ok("Compose logs"));
        }

        public Task<CommandResponse<string>> ExecuteAsync(DriverContext context, string composeFile, string service, string[] command, CancellationToken cancellationToken = default)
        {
            RecordCall(nameof(ExecuteAsync), context, composeFile, service, command);

            if (SimulateFailure)
                return Task.FromResult(CommandResponse<string>.Fail(FailureMessage, FailureErrorCode));

            return Task.FromResult(CommandResponse<string>.Ok("Execution output"));
        }

        private void RecordCall(string methodName, params object[] args)
        {
            _methodCalls.Add(new MethodCall { MethodName = methodName, Arguments = args, Timestamp = DateTime.UtcNow });
        }

        public void ClearMethodCalls()
        {
            _methodCalls.Clear();
        }

        public void ClearAll()
        {
            _containers.Clear();
            _images.Clear();
            _networks.Clear();
            _volumes.Clear();
            _methodCalls.Clear();
        }
    }

    public class MethodCall
    {
        public string MethodName { get; set; }
        public object[] Arguments { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
