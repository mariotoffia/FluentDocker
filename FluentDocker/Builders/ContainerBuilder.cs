using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
    /// <summary>
    /// Container builder implementation.
    /// </summary>
    internal partial class ContainerBuilder : IContainerBuilder, IDriverScopedBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;

        /// <inheritdoc />
        FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

        /// <inheritdoc />
        string IDriverScopedBuilder.DriverId => _driverId;
        private string _image;
        private string _name;
        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _ports = new Dictionary<string, string>();
        private readonly List<string> _command = new List<string>();
        private readonly Dictionary<string, string> _volumes = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        private readonly List<string> _networks = new List<string>();
        private readonly List<NetworkAlias> _networkAliases = new List<NetworkAlias>();
        private readonly List<WaitCondition> _waitConditions = new List<WaitCondition>();
        private readonly List<LifecycleHook> _lifecycleHooks = new List<LifecycleHook>();
        private readonly List<ContainerLink> _links = new List<ContainerLink>();

        private string _workingDir;
        private string _user;
        private string _restartPolicy;
        private string _hostname;
        private string _networkMode;
        private string _ipv4Address;
        private string _ipv6Address;
        private long? _memoryLimit;
        private long? _cpuShares;
        private bool _privileged;
        private bool _autoRemove;
        private bool _keepContainer;
        private bool _keepRunning;
        private bool _deleteVolumeOnDispose;
        private bool _deleteNamedVolumeOnDispose;
        private bool _forcePullImage;
        private ContainerExistsBehavior _existsBehavior = ContainerExistsBehavior.Default;
        private bool _destroyForce;
        private bool _destroyRemoveVolumes;
        private Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> _customResolver;
        private string _pod;
        private Services.Impl.ContainerService _pendingService;
        private bool _waitConditionsExecuted;

        public ContainerBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        #region Basic Configuration

        public IContainerBuilder UseImage(string image) { _image = image; return this; }
        public IContainerBuilder WithName(string name) { _name = name; return this; }

        public IContainerBuilder WithEnvironment(string key, string value)
        {
            _environment[key] = value;
            return this;
        }

        public IContainerBuilder WithEnvironment(string keyValue)
        {
            var parts = keyValue.Split(new[] { '=' }, 2);
            if (parts.Length == 2)
                _environment[parts[0]] = parts[1];
            else
                _environment[keyValue] = string.Empty;
            return this;
        }

        public IContainerBuilder WithPort(string containerPort, string hostPort)
        {
            _ports[containerPort] = hostPort;
            return this;
        }

        public IContainerBuilder ExposePort(string containerPort)
        {
            var normalized = containerPort.Contains("/") ? containerPort : $"{containerPort}/tcp";
            _ports[normalized] = "";
            return this;
        }

        public IContainerBuilder ExposePort(int hostPort, int containerPort)
        {
            _ports[$"{containerPort}/tcp"] = hostPort.ToString();
            return this;
        }

        public IContainerBuilder WithCommand(params string[] command) { _command.AddRange(command); return this; }
        public IContainerBuilder WithVolume(string hostPath, string containerPath) { _volumes[hostPath] = containerPath; return this; }
        public IContainerBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
        public IContainerBuilder WithWorkingDirectory(string workingDir) { _workingDir = workingDir; return this; }
        public IContainerBuilder WithUser(string user) { _user = user; return this; }
        public IContainerBuilder WithRestartPolicy(string policy) { _restartPolicy = policy; return this; }
        public IContainerBuilder WithHostname(string hostname) { _hostname = hostname; return this; }
        public IContainerBuilder WithNetworkMode(string networkMode) { _networkMode = networkMode; return this; }
        public IContainerBuilder WithNetwork(string networkName) { _networks.Add(networkName); return this; }

        public IContainerBuilder WithNetworkAlias(string networkName, string alias)
        {
            _networkAliases.Add(new NetworkAlias { NetworkName = networkName, Alias = alias });
            if (!_networks.Contains(networkName))
                _networks.Add(networkName);
            return this;
        }

        public IContainerBuilder UseIpV4(string ipv4Address) { _ipv4Address = ipv4Address; return this; }
        public IContainerBuilder UseIpV6(string ipv6Address) { _ipv6Address = ipv6Address; return this; }
        public IContainerBuilder WithMemoryLimit(long bytes) { _memoryLimit = bytes; return this; }
        public IContainerBuilder WithCpuShares(long shares) { _cpuShares = shares; return this; }
        public IContainerBuilder WithPrivileged(bool privileged = true) { _privileged = privileged; return this; }
        public IContainerBuilder WithAutoRemove(bool autoRemove = true) { _autoRemove = autoRemove; return this; }

        public IContainerBuilder WithLink(string containerName, string alias = null)
        {
            _links.Add(new ContainerLink { ContainerName = containerName, Alias = alias ?? containerName });
            return this;
        }

        public IContainerBuilder WithLinks(params string[] containerNames)
        {
            foreach (var name in containerNames)
                _links.Add(new ContainerLink { ContainerName = name, Alias = name });
            return this;
        }

        public IContainerBuilder WithPod(string podName) { _pod = podName; return this; }

        #endregion

        #region Container Existence Behavior

        public IContainerBuilder ReuseIfExists() { _existsBehavior = ContainerExistsBehavior.Reuse; return this; }

        public IContainerBuilder DestroyIfExists(bool force = false, bool removeVolumes = false)
        {
            _existsBehavior = ContainerExistsBehavior.Destroy;
            _destroyForce = force;
            _destroyRemoveVolumes = removeVolumes;
            return this;
        }

        public IContainerBuilder ForcePullImage() { _forcePullImage = true; return this; }

        #endregion

        #region Wait Conditions

        public IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Port,
                Target = portAndProto.Contains("/") ? portAndProto : $"{portAndProto}/tcp",
                TimeoutMs = timeoutMs
            });
            return this;
        }

        public IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Port,
                Target = portAndProto.Contains("/") ? portAndProto : $"{portAndProto}/tcp",
                Path = address,
                TimeoutMs = timeoutMs
            });
            return this;
        }

        public IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition { Type = WaitConditionType.Process, Target = processName, TimeoutMs = timeoutMs });
            return this;
        }

        public IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Http,
                Target = portAndProto.Contains("/") ? portAndProto : $"{portAndProto}/tcp",
                Path = path, TimeoutMs = timeoutMs, HttpMethod = HttpMethod.Get
            });
            return this;
        }

        public IContainerBuilder WaitForHttp(string url, long timeoutMs = 30000,
            HttpMethod method = null, string contentType = null, string body = null,
            Func<RequestResponse, int, long> continuation = null)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Http, Target = url, TimeoutMs = timeoutMs,
                HttpMethod = method ?? HttpMethod.Get, ContentType = contentType,
                Body = body, HttpContinuation = continuation
            });
            return this;
        }

        public IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition { Type = WaitConditionType.LogMessage, Target = message, TimeoutMs = timeoutMs });
            return this;
        }

        public IContainerBuilder WaitForHealthy(long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition { Type = WaitConditionType.Healthy, TimeoutMs = timeoutMs });
            return this;
        }

        public IContainerBuilder Wait(Func<IContainerService, int, int> condition)
        {
            _waitConditions.Add(new WaitCondition { Type = WaitConditionType.Lambda, LambdaCondition = condition, TimeoutMs = 60000 });
            return this;
        }

        #endregion

        #region Lifecycle Hooks

        public IContainerBuilder CopyToOnStart(string hostPath, string containerPath)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.CopyTo, TriggerState = ServiceRunningState.Running,
                HostPath = hostPath, ContainerPath = containerPath
            });
            return this;
        }

        public IContainerBuilder CopyFromOnDispose(string containerPath, string hostPath)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.CopyFrom, TriggerState = ServiceRunningState.Removing,
                HostPath = hostPath, ContainerPath = containerPath
            });
            return this;
        }

        public IContainerBuilder ExportOnDispose(string hostPath, bool explode = false)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Export, TriggerState = ServiceRunningState.Removing,
                HostPath = hostPath, Explode = explode, Condition = _ => true
            });
            return this;
        }

        public IContainerBuilder ExportOnDispose(string hostPath, Func<IContainerService, bool> condition, bool explode = false)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Export, TriggerState = ServiceRunningState.Removing,
                HostPath = hostPath, Explode = explode, Condition = condition
            });
            return this;
        }

        public IContainerBuilder ExecuteOnRunning(params string[] command)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Execute, TriggerState = ServiceRunningState.Running, Command = command
            });
            return this;
        }

        public IContainerBuilder ExecuteOnDisposing(params string[] command)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Execute, TriggerState = ServiceRunningState.Removing, Command = command
            });
            return this;
        }

        #endregion

        #region Dispose Behavior

        public IContainerBuilder KeepContainer() { _keepContainer = true; return this; }
        public IContainerBuilder KeepRunning() { _keepRunning = true; return this; }
        public IContainerBuilder DeleteVolumeOnDispose() { _deleteVolumeOnDispose = true; return this; }
        public IContainerBuilder DeleteNamedVolumeOnDispose() { _deleteNamedVolumeOnDispose = true; return this; }

        #endregion

        #region Advanced

        public IContainerBuilder UseCustomResolver(
            Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> resolver)
        {
            _customResolver = resolver;
            return this;
        }

        #endregion

        #region Execute

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            var driver = _kernel.SysCtl<Drivers.IContainerDriver>(_driverId);
            var imageDriver = _kernel.SysCtl<Drivers.IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            // Handle existing container
            if (!string.IsNullOrEmpty(_name) && _existsBehavior != ContainerExistsBehavior.Default)
            {
                var existing = await FindExistingContainerAsync(driver, context, _name, cancellationToken);
                if (existing != null)
                {
                    if (_existsBehavior == ContainerExistsBehavior.Reuse)
                    {
                        var reuseService = new Services.Impl.ContainerService(
                            _kernel, _driverId, existing, _image, _name,
                            !_keepRunning, !_keepContainer,
                            _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                            _customResolver, _lifecycleHooks);

                        var inspectResult = await driver.InspectAsync(context, existing, cancellationToken);
                        if (inspectResult.Success && inspectResult.Data?.State?.Running != true)
                            await reuseService.StartAsync(cancellationToken);

                        return reuseService;
                    }
                    else if (_existsBehavior == ContainerExistsBehavior.Destroy)
                    {
                        await driver.RemoveAsync(context, existing, _destroyForce, _destroyRemoveVolumes, cancellationToken);
                    }
                }
            }

            if (_forcePullImage && imageDriver != null)
                await imageDriver.PullAsync(context, _image, "latest", null, cancellationToken);

            var config = new Drivers.ContainerCreateConfig
            {
                Image = _image, Name = _name, Environment = _environment,
                PortBindings = _ports,
                Command = _command.Count > 0 ? _command.ToArray() : null,
                Labels = _labels.Count > 0 ? _labels : null,
                Volumes = _volumes.Count > 0 ? _volumes : null,
                Networks = _networks.Count > 0 ? _networks : null,
                WorkingDirectory = _workingDir, User = _user,
                RestartPolicy = _restartPolicy, Hostname = _hostname,
                NetworkMode = _networkMode, Ipv4Address = _ipv4Address,
                Ipv6Address = _ipv6Address, MemoryLimit = _memoryLimit,
                CpuShares = _cpuShares, Privileged = _privileged, AutoRemove = _autoRemove,
                Links = _links.Count > 0
                    ? _links.Select(l => l.Alias != l.ContainerName
                        ? $"{l.ContainerName}:{l.Alias}" : l.ContainerName).ToList()
                    : null,
                Pod = _pod
            };

            var response = await driver.CreateAsync(context, config, cancellationToken);
            if (!response.Success)
                throw new DriverException($"Failed to create container: {response.Error}",
                    response.ErrorCode, response.ErrorContext);

            var service = new Services.Impl.ContainerService(
                _kernel, _driverId, response.Data.Id, _image, _name,
                !_keepRunning, !_keepContainer,
                _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                _customResolver, _lifecycleHooks);

            _pendingService = service;
            bool hasLinks = _links.Count > 0;

            if (!hasLinks)
            {
                await service.StartAsync(cancellationToken);
                await WaitForContainerRunningAsync(driver, context, response.Data.Id, cancellationToken);
                await ExecuteLifecycleHooksAsync(service, ServiceRunningState.Running, cancellationToken);
                await ExecuteWaitConditionsAsync(service, cancellationToken);
                _waitConditionsExecuted = true;
            }

            return service;
        }

        private async Task WaitForContainerRunningAsync(
            Drivers.IContainerDriver driver, DriverContext context,
            string containerId, CancellationToken cancellationToken)
        {
            const int maxAttempts = 30;
            const int delayMs = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                var inspectResult = await driver.InspectAsync(context, containerId, cancellationToken);
                if (inspectResult.Success && inspectResult.Data?.State?.Running == true)
                    return;
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        private async Task<string> FindExistingContainerAsync(
            Drivers.IContainerDriver driver, DriverContext context,
            string name, CancellationToken cancellationToken)
        {
            var listResult = await driver.ListAsync(context,
                new Drivers.ContainerListFilter { All = true, Name = name }, cancellationToken);
            if (!listResult.Success) return null;

            var normalizedName = name.StartsWith("/") ? name.Substring(1) : name;
            var container = listResult.Data?.FirstOrDefault(c =>
            {
                var containerName = c.Name?.TrimStart('/');
                return string.Equals(containerName, normalizedName, StringComparison.OrdinalIgnoreCase);
            });
            return container?.Id;
        }

        #endregion
    }
}
