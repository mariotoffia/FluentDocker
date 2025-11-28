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
using FluentDocker.Model.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Builders.V3
{
    /// <summary>
    /// v3.0.0 async builder with WithinDriver() scoping and terminal BuildAsync().
    /// </summary>
    public class Builder : IFluentBuilder
    {
        private FluentDockerKernel _currentKernel;
        private string _currentDriverId;
        private readonly List<BuildOperation> _operations = new List<BuildOperation>();

        /// <summary>
        /// Creates a new builder.
        /// </summary>
        public Builder()
        {
        }

        /// <summary>
        /// Establishes a driver scope for subsequent operations.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="kernel">Kernel instance (reuses previous if null)</param>
        /// <returns>This builder for fluent chaining</returns>
        public Builder WithinDriver(string driverId, FluentDockerKernel kernel = null)
        {
            _currentKernel = kernel ?? _currentKernel;

            if (_currentKernel == null)
            {
                throw new InvalidOperationException(
                    "Kernel required in first WithinDriver() call. " +
                    "Provide a kernel or create one with FluentDockerKernel.Create().BuildAsync()");
            }

            _currentDriverId = driverId;
            return this;
        }

        /// <summary>
        /// Adds a container operation to the current scope.
        /// </summary>
        /// <param name="configure">Container configuration action</param>
        /// <returns>This builder for fluent chaining</returns>
        public Builder UseContainer(Action<IContainerBuilder> configure)
        {
            ValidateScope();

            var builder = new ContainerBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });

            return this;
        }

        /// <summary>
        /// Adds a network operation to the current scope.
        /// </summary>
        /// <param name="configure">Network configuration action</param>
        /// <returns>This builder for fluent chaining</returns>
        public Builder UseNetwork(Action<INetworkBuilder> configure)
        {
            ValidateScope();

            var builder = new NetworkBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });

            return this;
        }

        /// <summary>
        /// Adds a volume operation to the current scope.
        /// </summary>
        /// <param name="configure">Volume configuration action</param>
        /// <returns>This builder for fluent chaining</returns>
        public Builder UseVolume(Action<IVolumeBuilder> configure)
        {
            ValidateScope();

            var builder = new VolumeBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });

            return this;
        }

        /// <summary>
        /// Adds a compose operation to the current scope.
        /// </summary>
        /// <param name="configure">Compose configuration action</param>
        /// <returns>This builder for fluent chaining</returns>
        public Builder UseCompose(Action<IComposeBuilder> configure)
        {
            ValidateScope();

            var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
            configure(builder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });

            return this;
        }

        /// <summary>
        /// TERMINAL - Builds all operations synchronously.
        /// </summary>
        /// <remarks>
        /// For async contexts (ASP.NET, UI applications), prefer <see cref="BuildAsync"/> to avoid deadlocks.
        /// This method is safe to use in console apps, test fixtures, and scripts.
        /// </remarks>
        /// <returns>Build results containing all services</returns>
        public BuildResults Build()
        {
            // Use Task.Run to avoid deadlocks in sync-over-async scenarios
            return Task.Run(() => BuildAsync()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// TERMINAL - Builds all operations asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Build results containing all services</returns>
        public async Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default)
        {
            var scopes = new Dictionary<(FluentDockerKernel, string), BuildScope>();

            // Group operations by scope (kernel + driver)
            var groupedOps = _operations.GroupBy(op => (op.Kernel, op.DriverId));

            foreach (var group in groupedOps)
            {
                var key = group.Key;
                var scope = new BuildScope(key.Kernel, key.DriverId);
                scopes[key] = scope;

                // Execute operations in this scope
                foreach (var operation in group)
                {
                    var service = await operation.ExecuteAsync(cancellationToken);
                    scope.AddResult(service);
                }
            }

            return new BuildResults(scopes.Values.ToList());
        }

        private void ValidateScope()
        {
            if (_currentKernel == null || _currentDriverId == null)
            {
                throw new InvalidOperationException(
                    "Must call WithinDriver() before adding operations");
            }
        }
    }

    /// <summary>
    /// Represents a build operation to be executed.
    /// </summary>
    internal class BuildOperation
    {
        public FluentDockerKernel Kernel { get; set; }
        public string DriverId { get; set; }
        public Func<CancellationToken, Task<IService>> ExecuteAsync { get; set; }
    }

    /// <summary>
    /// Interface for the v3.0.0 fluent builder.
    /// </summary>
    public interface IFluentBuilder
    {
        Builder WithinDriver(string driverId, FluentDockerKernel kernel = null);
        Builder UseContainer(Action<IContainerBuilder> configure);
        Builder UseNetwork(Action<INetworkBuilder> configure);
        Builder UseVolume(Action<IVolumeBuilder> configure);
        Builder UseCompose(Action<IComposeBuilder> configure);
        
        /// <summary>
        /// Builds all operations synchronously (TERMINAL operation).
        /// For async contexts, prefer BuildAsync() to avoid deadlocks.
        /// </summary>
        BuildResults Build();
        
        /// <summary>
        /// Builds all operations asynchronously (TERMINAL operation).
        /// </summary>
        Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default);
    }

    #region Wait Condition Types

    /// <summary>
    /// Defines a wait condition for the container.
    /// </summary>
    internal enum WaitConditionType
    {
        Port,
        Process,
        Http,
        LogMessage,
        Healthy,
        Lambda
    }

    /// <summary>
    /// Represents a wait condition configuration.
    /// </summary>
    internal class WaitCondition
    {
        public WaitConditionType Type { get; set; }
        public string Target { get; set; }
        public string Path { get; set; }
        public long TimeoutMs { get; set; }
        public HttpMethod HttpMethod { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public Func<RequestResponse, int, long> HttpContinuation { get; set; }
        public Func<IContainerService, int, int> LambdaCondition { get; set; }
    }

    /// <summary>
    /// Lifecycle hook types.
    /// </summary>
    public enum LifecycleHookType
    {
        CopyTo,
        CopyFrom,
        Export,
        Execute
    }

    /// <summary>
    /// Represents a lifecycle hook configuration.
    /// </summary>
    public class LifecycleHook
    {
        public LifecycleHookType Type { get; set; }
        public ServiceRunningState TriggerState { get; set; }
        public string HostPath { get; set; }
        public string ContainerPath { get; set; }
        public string[] Command { get; set; }
        public bool Explode { get; set; }
        public Func<IContainerService, bool> Condition { get; set; }
    }

    /// <summary>
    /// Container existence behavior when name conflicts.
    /// </summary>
    public enum ContainerExistsBehavior
    {
        /// <summary>Do nothing if container exists - will fail on create.</summary>
        Default,
        /// <summary>Reuse the existing container.</summary>
        Reuse,
        /// <summary>Destroy the existing container and create new.</summary>
        Destroy
    }

    /// <summary>
    /// Network alias configuration.
    /// </summary>
    public class NetworkAlias
    {
        public string NetworkName { get; set; }
        public string Alias { get; set; }
    }

    #endregion

    #region Container Builder Interface

    /// <summary>
    /// Container builder for lambda configuration.
    /// </summary>
    public interface IContainerBuilder
    {
        #region Basic Configuration
        
        IContainerBuilder UseImage(string image);
        IContainerBuilder WithName(string name);
        IContainerBuilder WithEnvironment(string key, string value);
        /// <summary>Sets an environment variable using "KEY=VALUE" format.</summary>
        IContainerBuilder WithEnvironment(string keyValue);
        IContainerBuilder WithPort(string containerPort, string hostPort);
        /// <summary>Exposes a container port, letting Docker assign a random host port.</summary>
        IContainerBuilder ExposePort(string containerPort);
        /// <summary>Exposes a container port with explicit host port mapping.</summary>
        IContainerBuilder ExposePort(int hostPort, int containerPort);
        IContainerBuilder WithCommand(params string[] command);
        IContainerBuilder WithVolume(string hostPath, string containerPath);
        IContainerBuilder WithLabel(string key, string value);
        IContainerBuilder WithWorkingDirectory(string workingDir);
        IContainerBuilder WithUser(string user);
        IContainerBuilder WithRestartPolicy(string policy);
        IContainerBuilder WithHostname(string hostname);
        IContainerBuilder WithNetworkMode(string networkMode);
        IContainerBuilder WithNetwork(string networkName);
        /// <summary>Adds the container to a network with a DNS alias.</summary>
        IContainerBuilder WithNetworkAlias(string networkName, string alias);
        IContainerBuilder WithMemoryLimit(long bytes);
        IContainerBuilder WithCpuShares(long shares);
        IContainerBuilder WithPrivileged(bool privileged = true);
        IContainerBuilder WithAutoRemove(bool autoRemove = true);
        
        #endregion

        #region Container Existence Behavior
        
        /// <summary>If container with same name exists, reuse it instead of creating new.</summary>
        IContainerBuilder ReuseIfExists();
        
        /// <summary>If container with same name exists, destroy it before creating new.</summary>
        /// <param name="force">Force remove even if running.</param>
        /// <param name="removeVolumes">Remove associated volumes.</param>
        IContainerBuilder DestroyIfExists(bool force = false, bool removeVolumes = false);
        
        /// <summary>Always pull the image before creating container.</summary>
        IContainerBuilder ForcePullImage();
        
        #endregion

        #region Wait Conditions
        
        /// <summary>Wait for a port to be available after starting.</summary>
        IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000);
        
        /// <summary>Wait for a port with custom address.</summary>
        IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000);
        
        /// <summary>Wait for a process to be running after starting.</summary>
        IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000);
        
        /// <summary>Wait for an HTTP endpoint to respond after starting.</summary>
        IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000);
        
        /// <summary>Wait for an HTTP endpoint with advanced options.</summary>
        IContainerBuilder WaitForHttp(
            string url, 
            long timeoutMs = 30000,
            HttpMethod method = null,
            string contentType = null,
            string body = null,
            Func<RequestResponse, int, long> continuation = null);
        
        /// <summary>Wait for a specific message in logs after starting.</summary>
        IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000);
        
        /// <summary>Wait for container to be healthy (Docker HEALTHCHECK).</summary>
        IContainerBuilder WaitForHealthy(long timeoutMs = 30000);
        
        /// <summary>Custom wait condition lambda.</summary>
        /// <param name="condition">Function that returns poll interval in ms, or 0 to continue, or -1 to succeed.</param>
        IContainerBuilder Wait(Func<IContainerService, int, int> condition);
        
        #endregion

        #region Lifecycle Hooks
        
        /// <summary>Copy file/folder to container after it starts.</summary>
        IContainerBuilder CopyToOnStart(string hostPath, string containerPath);
        
        /// <summary>Copy file/folder from container before it's disposed.</summary>
        IContainerBuilder CopyFromOnDispose(string containerPath, string hostPath);
        
        /// <summary>Export container filesystem on dispose.</summary>
        IContainerBuilder ExportOnDispose(string hostPath, bool explode = false);
        
        /// <summary>Export container filesystem on dispose with condition.</summary>
        IContainerBuilder ExportOnDispose(string hostPath, Func<IContainerService, bool> condition, bool explode = false);
        
        /// <summary>Execute command in container after it starts.</summary>
        IContainerBuilder ExecuteOnRunning(params string[] command);
        
        /// <summary>Execute command in container before it's disposed.</summary>
        IContainerBuilder ExecuteOnDisposing(params string[] command);
        
        #endregion

        #region Dispose Behavior
        
        /// <summary>Keeps the container after dispose (don't delete).</summary>
        IContainerBuilder KeepContainer();
        
        /// <summary>Keeps the container running after dispose (don't stop).</summary>
        IContainerBuilder KeepRunning();
        
        /// <summary>Delete anonymous volumes when container is disposed.</summary>
        IContainerBuilder DeleteVolumeOnDispose();
        
        /// <summary>Delete named volumes when container is disposed.</summary>
        IContainerBuilder DeleteNamedVolumeOnDispose();
        
        #endregion

        #region Advanced
        
        /// <summary>Use custom endpoint resolver for port mapping.</summary>
        IContainerBuilder UseCustomResolver(
            Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> resolver);
        
        #endregion
    }

    #endregion

    #region Other Builder Interfaces

    /// <summary>
    /// Network builder for lambda configuration.
    /// </summary>
    public interface INetworkBuilder
    {
        INetworkBuilder WithName(string name);
        INetworkBuilder UseDriver(string driver);
        INetworkBuilder WithSubnet(string subnet);
        INetworkBuilder WithGateway(string gateway);
        INetworkBuilder WithIPRange(string ipRange);
        INetworkBuilder WithIPv6(bool enableIPv6 = true);
        INetworkBuilder AsInternal(bool isInternal = true);
        INetworkBuilder WithLabel(string key, string value);
        INetworkBuilder WithOption(string key, string value);
        /// <summary>Remove network on dispose.</summary>
        INetworkBuilder RemoveOnDispose();
    }

    /// <summary>
    /// Volume builder for lambda configuration.
    /// </summary>
    public interface IVolumeBuilder
    {
        IVolumeBuilder WithName(string name);
        IVolumeBuilder UseDriver(string driver);
        IVolumeBuilder WithDriverOption(string key, string value);
        IVolumeBuilder WithLabel(string key, string value);
        /// <summary>Remove volume on dispose.</summary>
        IVolumeBuilder RemoveOnDispose();
    }

    /// <summary>
    /// Compose builder for lambda configuration.
    /// </summary>
    public interface IComposeBuilder
    {
        IComposeBuilder WithComposeFile(string path);
        IComposeBuilder WithProjectName(string name);
        IComposeBuilder WithEnvironment(string key, string value);
        IComposeBuilder WithBuild(bool build = true);
        IComposeBuilder WithForceRecreate(bool forceRecreate = true);
        IComposeBuilder WithRemoveOrphans(bool removeOrphans = true);
        IComposeBuilder ForServices(params string[] services);
        /// <summary>Remove volumes on down.</summary>
        IComposeBuilder WithRemoveVolumes(bool removeVolumes = true);
        /// <summary>Remove images on down.</summary>
        IComposeBuilder WithRemoveImages(bool removeImages = true);
    }

    #endregion

    #region Container Builder Implementation

    /// <summary>
    /// Container builder implementation.
    /// </summary>
    internal class ContainerBuilder : IContainerBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
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
        
        private string _workingDir;
        private string _user;
        private string _restartPolicy;
        private string _hostname;
        private string _networkMode;
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

        public ContainerBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        #region Basic Configuration

        public IContainerBuilder UseImage(string image)
        {
            _image = image;
            return this;
        }

        public IContainerBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

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

        public IContainerBuilder WithCommand(params string[] command)
        {
            _command.AddRange(command);
            return this;
        }

        public IContainerBuilder WithVolume(string hostPath, string containerPath)
        {
            _volumes[hostPath] = containerPath;
            return this;
        }

        public IContainerBuilder WithLabel(string key, string value)
        {
            _labels[key] = value;
            return this;
        }

        public IContainerBuilder WithWorkingDirectory(string workingDir)
        {
            _workingDir = workingDir;
            return this;
        }

        public IContainerBuilder WithUser(string user)
        {
            _user = user;
            return this;
        }

        public IContainerBuilder WithRestartPolicy(string policy)
        {
            _restartPolicy = policy;
            return this;
        }

        public IContainerBuilder WithHostname(string hostname)
        {
            _hostname = hostname;
            return this;
        }

        public IContainerBuilder WithNetworkMode(string networkMode)
        {
            _networkMode = networkMode;
            return this;
        }

        public IContainerBuilder WithNetwork(string networkName)
        {
            _networks.Add(networkName);
            return this;
        }

        public IContainerBuilder WithNetworkAlias(string networkName, string alias)
        {
            _networkAliases.Add(new NetworkAlias { NetworkName = networkName, Alias = alias });
            if (!_networks.Contains(networkName))
            _networks.Add(networkName);
            return this;
        }

        public IContainerBuilder WithMemoryLimit(long bytes)
        {
            _memoryLimit = bytes;
            return this;
        }

        public IContainerBuilder WithCpuShares(long shares)
        {
            _cpuShares = shares;
            return this;
        }

        public IContainerBuilder WithPrivileged(bool privileged = true)
        {
            _privileged = privileged;
            return this;
        }

        public IContainerBuilder WithAutoRemove(bool autoRemove = true)
        {
            _autoRemove = autoRemove;
            return this;
        }

        #endregion

        #region Container Existence Behavior

        public IContainerBuilder ReuseIfExists()
        {
            _existsBehavior = ContainerExistsBehavior.Reuse;
            return this;
        }

        public IContainerBuilder DestroyIfExists(bool force = false, bool removeVolumes = false)
        {
            _existsBehavior = ContainerExistsBehavior.Destroy;
            _destroyForce = force;
            _destroyRemoveVolumes = removeVolumes;
            return this;
        }

        public IContainerBuilder ForcePullImage()
        {
            _forcePullImage = true;
            return this;
        }

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
                Path = address, // Using Path to store address
                TimeoutMs = timeoutMs
            });
            return this;
        }

        public IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Process,
                Target = processName,
                TimeoutMs = timeoutMs
            });
            return this;
        }

        public IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Http,
                Target = portAndProto.Contains("/") ? portAndProto : $"{portAndProto}/tcp",
                Path = path,
                TimeoutMs = timeoutMs,
                HttpMethod = HttpMethod.Get
            });
            return this;
        }

        public IContainerBuilder WaitForHttp(
            string url,
            long timeoutMs = 30000,
            HttpMethod method = null,
            string contentType = null,
            string body = null,
            Func<RequestResponse, int, long> continuation = null)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Http,
                Target = url, // Full URL
                TimeoutMs = timeoutMs,
                HttpMethod = method ?? HttpMethod.Get,
                ContentType = contentType,
                Body = body,
                HttpContinuation = continuation
            });
            return this;
        }

        public IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.LogMessage,
                Target = message,
                TimeoutMs = timeoutMs
            });
            return this;
        }

        public IContainerBuilder WaitForHealthy(long timeoutMs = 30000)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Healthy,
                TimeoutMs = timeoutMs
            });
            return this;
        }

        public IContainerBuilder Wait(Func<IContainerService, int, int> condition)
        {
            _waitConditions.Add(new WaitCondition
            {
                Type = WaitConditionType.Lambda,
                LambdaCondition = condition,
                TimeoutMs = 60000 // Default 1 minute for lambda
            });
            return this;
        }

        #endregion

        #region Lifecycle Hooks

        public IContainerBuilder CopyToOnStart(string hostPath, string containerPath)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.CopyTo,
                TriggerState = ServiceRunningState.Running,
                HostPath = hostPath,
                ContainerPath = containerPath
            });
            return this;
        }

        public IContainerBuilder CopyFromOnDispose(string containerPath, string hostPath)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.CopyFrom,
                TriggerState = ServiceRunningState.Removing,
                HostPath = hostPath,
                ContainerPath = containerPath
            });
            return this;
        }

        public IContainerBuilder ExportOnDispose(string hostPath, bool explode = false)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Export,
                TriggerState = ServiceRunningState.Removing,
                HostPath = hostPath,
                Explode = explode,
                Condition = _ => true
            });
            return this;
        }

        public IContainerBuilder ExportOnDispose(string hostPath, Func<IContainerService, bool> condition, bool explode = false)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Export,
                TriggerState = ServiceRunningState.Removing,
                HostPath = hostPath,
                Explode = explode,
                Condition = condition
            });
            return this;
        }

        public IContainerBuilder ExecuteOnRunning(params string[] command)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Execute,
                TriggerState = ServiceRunningState.Running,
                Command = command
            });
            return this;
        }

        public IContainerBuilder ExecuteOnDisposing(params string[] command)
        {
            _lifecycleHooks.Add(new LifecycleHook
            {
                Type = LifecycleHookType.Execute,
                TriggerState = ServiceRunningState.Removing,
                Command = command
            });
            return this;
        }

        #endregion

        #region Dispose Behavior

        public IContainerBuilder KeepContainer()
        {
            _keepContainer = true;
            return this;
        }

        public IContainerBuilder KeepRunning()
        {
            _keepRunning = true;
            return this;
        }

        public IContainerBuilder DeleteVolumeOnDispose()
        {
            _deleteVolumeOnDispose = true;
            return this;
        }

        public IContainerBuilder DeleteNamedVolumeOnDispose()
        {
            _deleteNamedVolumeOnDispose = true;
            return this;
        }

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
                        // Return service wrapping existing container
                        var reuseService = new Services.Impl.ContainerService(
                            _kernel, _driverId, existing, _image, _name,
                            !_keepRunning, !_keepContainer,
                            _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                            _customResolver, _lifecycleHooks);
                        
                        // Start if not running
                        var inspectResult = await driver.InspectAsync(context, existing, cancellationToken);
                        if (inspectResult.Success && inspectResult.Data?.State?.Running != true)
                        {
                            await reuseService.StartAsync(cancellationToken);
                        }
                        
                        return reuseService;
                    }
                    else if (_existsBehavior == ContainerExistsBehavior.Destroy)
                    {
                        await driver.RemoveAsync(context, existing, _destroyForce, _destroyRemoveVolumes, cancellationToken);
                    }
                }
            }

            // Force pull image if requested
            if (_forcePullImage && imageDriver != null)
            {
                await imageDriver.PullAsync(context, _image, "latest", null, cancellationToken);
            }

            // Create container
            var config = new Drivers.ContainerCreateConfig
            {
                Image = _image,
                Name = _name,
                Environment = _environment,
                PortBindings = _ports,
                Command = _command.Count > 0 ? _command.ToArray() : null,
                Labels = _labels.Count > 0 ? _labels : null,
                Volumes = _volumes.Count > 0 ? _volumes : null,
                Networks = _networks.Count > 0 ? _networks : null,
                // Network aliases will be applied when connecting to networks
                WorkingDirectory = _workingDir,
                User = _user,
                RestartPolicy = _restartPolicy,
                Hostname = _hostname,
                NetworkMode = _networkMode,
                MemoryLimit = _memoryLimit,
                CpuShares = _cpuShares,
                Privileged = _privileged,
                AutoRemove = _autoRemove
            };

            var response = await driver.CreateAsync(context, config, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to create container: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            // Create service with all options
            var service = new Services.Impl.ContainerService(
                _kernel, _driverId, response.Data.Id, _image, _name,
                !_keepRunning, !_keepContainer,
                _deleteVolumeOnDispose, _deleteNamedVolumeOnDispose,
                _customResolver, _lifecycleHooks);

            // Start the container
            await service.StartAsync(cancellationToken);

            // Execute "on running" lifecycle hooks
            await ExecuteLifecycleHooksAsync(service, ServiceRunningState.Running, cancellationToken);

            // Execute wait conditions
            await ExecuteWaitConditionsAsync(service, cancellationToken);

            return service;
        }

        private async Task<string> FindExistingContainerAsync(
            Drivers.IContainerDriver driver,
            DriverContext context,
            string name,
            CancellationToken cancellationToken)
        {
            var listResult = await driver.ListAsync(context, new Drivers.ContainerListFilter { All = true, Name = name }, cancellationToken);
            if (!listResult.Success) return null;

            var normalizedName = name.StartsWith("/") ? name.Substring(1) : name;
            var container = listResult.Data?.FirstOrDefault(c => 
            {
                var containerName = c.Name?.TrimStart('/');
                return string.Equals(containerName, normalizedName, StringComparison.OrdinalIgnoreCase);
            });
            
            return container?.Id;
        }

        private async Task ExecuteLifecycleHooksAsync(
            Services.Impl.ContainerService service, 
            ServiceRunningState state,
            CancellationToken cancellationToken)
        {
            var hooks = _lifecycleHooks.Where(h => h.TriggerState == state);
            
            foreach (var hook in hooks)
            {
                switch (hook.Type)
                {
                    case LifecycleHookType.CopyTo:
                        await service.CopyToAsync(hook.ContainerPath, 
                            File.ReadAllBytes(hook.HostPath), cancellationToken);
                        break;
                        
                    case LifecycleHookType.Execute:
                        await service.ExecuteAsync(string.Join(" ", hook.Command), cancellationToken);
                        break;
                }
            }
        }

        private async Task ExecuteWaitConditionsAsync(
            Services.Impl.ContainerService service,
            CancellationToken cancellationToken)
        {
            foreach (var condition in _waitConditions)
            {
                bool success;
                switch (condition.Type)
                {
                    case WaitConditionType.Port:
                        if (!string.IsNullOrEmpty(condition.Path))
                        {
                            // Custom address specified
                            var hostPort = await service.GetHostPortAsync(condition.Target, cancellationToken);
                            success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                                condition.Path, hostPort, condition.TimeoutMs, cancellationToken);
                        }
                        else
                        {
                            success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                                service, condition.Target, condition.TimeoutMs, cancellationToken);
                        }
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for port {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.Process:
                        success = await Services.Extensions.ServiceExtensions.WaitForProcessAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for process {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.Http:
                        if (condition.Target.StartsWith("http://") || condition.Target.StartsWith("https://"))
                        {
                            // Full URL provided
                            success = await WaitForHttpUrlAsync(
                                condition.Target, condition.TimeoutMs, condition.HttpMethod,
                                condition.ContentType, condition.Body, condition.HttpContinuation,
                                cancellationToken);
                        }
                        else
                        {
                            success = await Services.Extensions.ServiceExtensions.WaitForHttpAsync(
                                service, condition.Target, condition.Path, condition.TimeoutMs, cancellationToken);
                        }
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for HTTP on container {service.Id}");
                        break;

                    case WaitConditionType.LogMessage:
                        success = await Services.Extensions.ServiceExtensions.WaitForLogMessageAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for log message '{condition.Target}' on container {service.Id}");
                        break;

                    case WaitConditionType.Healthy:
                        success = await WaitForHealthyAsync(service, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for container {service.Id} to be healthy");
                        break;

                    case WaitConditionType.Lambda:
                        success = await WaitForLambdaAsync(service, condition.LambdaCondition, 
                            condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new FluentDockerException(
                                $"Timeout waiting for custom condition on container {service.Id}");
                        break;
                }
            }
        }

        private async Task<bool> WaitForHealthyAsync(
            Services.Impl.ContainerService service,
            long timeoutMs,
            CancellationToken cancellationToken)
        {
            var start = DateTime.UtcNow;
            var elapsed = 0L;

            while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
            {
                var config = await service.InspectAsync(cancellationToken);
                var health = config?.State?.Health?.Status;
                
                if (health == Model.Containers.HealthState.Healthy)
                    return true;
                    
                if (health == Model.Containers.HealthState.Unhealthy)
                    return false;

                await Task.Delay(1000, cancellationToken);
                elapsed = (long)(DateTime.UtcNow - start).TotalMilliseconds;
            }

            return false;
        }

        private async Task<bool> WaitForLambdaAsync(
            IContainerService service,
            Func<IContainerService, int, int> condition,
            long timeoutMs,
            CancellationToken cancellationToken)
        {
            var start = DateTime.UtcNow;
            var elapsed = 0L;
            var iteration = 0;

            while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
            {
                var result = condition(service, iteration++);
                
                if (result < 0) // Success
                    return true;
                    
                if (result == 0) // Continue immediately
                    continue;

                // Wait for specified interval
                await Task.Delay(result, cancellationToken);
                elapsed = (long)(DateTime.UtcNow - start).TotalMilliseconds;
            }

            return false;
        }

        private async Task<bool> WaitForHttpUrlAsync(
            string url,
            long timeoutMs,
            HttpMethod method,
            string contentType,
            string body,
            Func<RequestResponse, int, long> continuation,
            CancellationToken cancellationToken)
        {
            var start = DateTime.UtcNow;
            var elapsed = 0L;
            var iteration = 0;

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
                    
                    if (!string.IsNullOrEmpty(body))
                    {
                        request.Content = new StringContent(body);
                        if (!string.IsNullOrEmpty(contentType))
                            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    }

                    var response = await httpClient.SendAsync(request, cancellationToken);
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (continuation != null)
                    {
                        // Create RequestResponse using internal constructor via reflection or just check status
                        var delay = continuation(
                            new RequestResponse(response.Headers, response.StatusCode, responseBody, null), 
                            iteration++);
                        if (delay < 0) // Success
                            return true;
                        if (delay > 0)
                            await Task.Delay((int)delay, cancellationToken);
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not ready yet
                }
                catch (TaskCanceledException)
                {
                    // Timeout on request
                }

                await Task.Delay(500, cancellationToken);
                elapsed = (long)(DateTime.UtcNow - start).TotalMilliseconds;
            }

            return false;
        }

        private async Task<int> GetHostPortAsync(
            IContainerService service, 
            string portAndProto, 
            CancellationToken cancellationToken)
        {
            var endpoint = await Services.Extensions.ServiceExtensions.ToHostExposedEndpointAsync(
                service, portAndProto, cancellationToken);
            return endpoint?.Port ?? 0;
        }

        #endregion
    }

    #endregion

    #region Network Builder Implementation

    /// <summary>
    /// Network builder implementation.
    /// </summary>
    internal class NetworkBuilder : INetworkBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private string _name;
        private string _driver = "bridge";
        private string _subnet;
        private string _gateway;
        private string _ipRange;
        private bool _enableIPv6;
        private bool _internal;
        private bool _removeOnDispose;
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _options = new Dictionary<string, string>();

        public NetworkBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        public INetworkBuilder WithName(string name) { _name = name; return this; }
        public INetworkBuilder UseDriver(string driver) { _driver = driver; return this; }
        public INetworkBuilder WithSubnet(string subnet) { _subnet = subnet; return this; }
        public INetworkBuilder WithGateway(string gateway) { _gateway = gateway; return this; }
        public INetworkBuilder WithIPRange(string ipRange) { _ipRange = ipRange; return this; }
        public INetworkBuilder WithIPv6(bool enableIPv6 = true) { _enableIPv6 = enableIPv6; return this; }
        public INetworkBuilder AsInternal(bool isInternal = true) { _internal = isInternal; return this; }
        public INetworkBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }
        public INetworkBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }
        public INetworkBuilder WithOption(string key, string value) { _options[key] = value; return this; }

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            var driver = _kernel.SysCtl<Drivers.INetworkDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var config = new Drivers.NetworkCreateConfig
            {
                Name = _name,
                Driver = _driver,
                Subnet = _subnet,
                Gateway = _gateway,
                EnableIPv6 = _enableIPv6,
                Internal = _internal,
                Labels = _labels,
                Options = _options
            };
            
            // IP range can be set via options if needed
            if (!string.IsNullOrEmpty(_ipRange))
                config.Options["com.docker.network.bridge.ip-range"] = _ipRange;

            var response = await driver.CreateAsync(context, config, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to create network: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return new Services.Impl.NetworkService(
                _kernel, _driverId, response.Data.Id, _name, _removeOnDispose);
        }
    }

    #endregion

    #region Volume Builder Implementation

    /// <summary>
    /// Volume builder implementation.
    /// </summary>
    internal class VolumeBuilder : IVolumeBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private string _name;
        private string _driver = "local";
        private bool _removeOnDispose;
        private readonly Dictionary<string, string> _driverOpts = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();

        public VolumeBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        public IVolumeBuilder WithName(string name) { _name = name; return this; }
        public IVolumeBuilder UseDriver(string driver) { _driver = driver; return this; }
        public IVolumeBuilder RemoveOnDispose() { _removeOnDispose = true; return this; }
        public IVolumeBuilder WithDriverOption(string key, string value) { _driverOpts[key] = value; return this; }
        public IVolumeBuilder WithLabel(string key, string value) { _labels[key] = value; return this; }

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            var driver = _kernel.SysCtl<Drivers.IVolumeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var config = new Drivers.VolumeCreateConfig
            {
                Name = _name,
                Driver = _driver,
                DriverOpts = _driverOpts,
                Labels = _labels
            };

            var response = await driver.CreateAsync(context, config, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to create volume: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return new Services.Impl.VolumeService(
                _kernel, _driverId, response.Data.Name, _driver, _removeOnDispose);
        }
    }

    #endregion

    #region Compose Builder Implementation

    /// <summary>
    /// Compose builder implementation.
    /// </summary>
    internal class ComposeBuilder : IComposeBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private readonly List<string> _composeFiles = new List<string>();
        private string _projectName;
        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
        private bool _build;
        private bool _forceRecreate;
        private bool _removeOrphans;
        private bool _removeVolumes;
        private bool _removeImages;
        private readonly List<string> _services = new List<string>();

        public ComposeBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        public IComposeBuilder WithComposeFile(string path) { _composeFiles.Add(path); return this; }
        public IComposeBuilder WithProjectName(string name) { _projectName = name; return this; }
        public IComposeBuilder WithEnvironment(string key, string value) { _environment[key] = value; return this; }
        public IComposeBuilder WithBuild(bool build = true) { _build = build; return this; }
        public IComposeBuilder WithForceRecreate(bool forceRecreate = true) { _forceRecreate = forceRecreate; return this; }
        public IComposeBuilder WithRemoveOrphans(bool removeOrphans = true) { _removeOrphans = removeOrphans; return this; }
        public IComposeBuilder WithRemoveVolumes(bool removeVolumes = true) { _removeVolumes = removeVolumes; return this; }
        public IComposeBuilder WithRemoveImages(bool removeImages = true) { _removeImages = removeImages; return this; }
        public IComposeBuilder ForServices(params string[] services) { _services.AddRange(services); return this; }

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            var driver = _kernel.SysCtl<Drivers.IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var config = new Drivers.ComposeUpConfig
            {
                ComposeFiles = _composeFiles,
                ProjectName = _projectName,
                Environment = _environment,
                Build = _build,
                ForceRecreate = _forceRecreate,
                RemoveOrphans = _removeOrphans,
                Services = _services,
                Detached = true
            };

            var response = await driver.UpAsync(context, config, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to start compose: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return new Services.Impl.ComposeService(
                _kernel, _driverId, _composeFiles, 
                response.Data.ProjectName ?? _projectName,
                _removeVolumes, _removeImages);
        }
    }

    #endregion
}
