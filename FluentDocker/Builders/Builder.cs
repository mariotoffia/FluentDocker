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

namespace FluentDocker.Builders
{
    /// <summary>
    /// v3.0.0 async builder with WithinDriver() scoping and terminal BuildAsync().
    /// </summary>
    public class Builder : IBuilder
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
        /// Adds an image build operation to the current scope.
        /// </summary>
        /// <param name="imageName">Name of the image to build</param>
        /// <param name="configure">Dockerfile configuration action</param>
        /// <returns>This builder for fluent chaining</returns>
        /// <example>
        /// <code>
        /// var results = await new Builder()
        ///     .WithinDriver("docker", kernel)
        ///     .UseImage("myapp:latest", img => img
        ///         .From("node:18")
        ///         .Run("npm install")
        ///         .Copy(".", "/app")
        ///         .Command("npm", "start"))
        ///     .UseContainer(c => c
        ///         .UseImage("myapp:latest")
        ///         .ExposePort(3000))
        ///     .BuildAsync();
        /// </code>
        /// </example>
        public Builder UseImage(string imageName, Action<DockerfileBuilder> configure)
        {
            ValidateScope();

            var imageBuilder = new ImageBuilder(_currentKernel, _currentDriverId, imageName);
            var dockerfileBuilder = imageBuilder.From();
            configure(dockerfileBuilder);

            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel,
                DriverId = _currentDriverId,
                ExecuteAsync = ct => imageBuilder.ExecuteAsync(ct).ContinueWith(t => (IService)t.Result, ct)
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
                // NOTE: Container operations with links will create but not start containers
                // They will be started later in dependency order
                foreach (var operation in group)
                {
                    var service = await operation.ExecuteAsync(cancellationToken);
                    scope.AddResult(service);
                }

                // After all services are created, start containers with links in dependency order
                await StartContainersWithLinksAsync(scope, cancellationToken);
            }

            return new BuildResults(scopes.Values.ToList());
        }

        /// <summary>
        /// Starts containers that have links in proper dependency order.
        /// Containers without links are already started. This method only starts containers
        /// that were created but not started due to link dependencies.
        ///
        /// Containers are started in the order they were created, which should respect
        /// dependency order (linked containers should be defined after their dependencies).
        /// </summary>
        private async Task StartContainersWithLinksAsync(BuildScope scope, CancellationToken cancellationToken)
        {
            // Find all containers that need to be started (they have links and are not running)
            // Keep them in the original order (Results list maintains creation order)
            var containersToStart = scope.Results
                .OfType<IContainerService>()
                .Where(c => c.State != ServiceRunningState.Running)
                .ToList();

            if (containersToStart.Count == 0)
                return;

            // Get driver to check container status
            var driver = scope.Kernel.SysCtl<Drivers.IContainerDriver>(scope.DriverId);
            var context = new DriverContext(scope.DriverId);

            // Start containers in order - each container must wait for its linked containers
            // to be running. By starting in creation order, dependencies should be satisfied.
            foreach (var container in containersToStart)
            {
                await container.StartAsync(cancellationToken);

                // Wait for container to actually be running before starting next one
                // This is critical for containers with links
                await WaitForContainerRunningAsync(driver, context, container.Id, cancellationToken);

                // Note: Lifecycle hooks are executed by ContainerService.StartAsync
            }
        }

        /// <summary>
        /// Waits for a container to be in running state.
        /// </summary>
        private async Task WaitForContainerRunningAsync(
            Drivers.IContainerDriver driver,
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken)
        {
            const int maxAttempts = 30; // 30 attempts * 100ms = 3 seconds max
            const int delayMs = 100;

            for (int i = 0; i < maxAttempts; i++)
            {
                var inspectResult = await driver.InspectAsync(context, containerId, cancellationToken);
                if (inspectResult.Success && inspectResult.Data?.State?.Running == true)
                {
                    return;
                }

                await Task.Delay(delayMs, cancellationToken);
            }

            // If we get here, container didn't start in time, but let Docker handle the error
            // when the next container with links tries to start
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
    public interface IBuilder
    {
        Builder WithinDriver(string driverId, FluentDockerKernel kernel = null);
        Builder UseContainer(Action<IContainerBuilder> configure);
        Builder UseNetwork(Action<INetworkBuilder> configure);
        Builder UseVolume(Action<IVolumeBuilder> configure);
        Builder UseCompose(Action<IComposeBuilder> configure);
        
        /// <summary>
        /// Adds an image build operation.
        /// </summary>
        Builder UseImage(string imageName, Action<DockerfileBuilder> configure);
        
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

    /// <summary>
    /// Container link configuration (legacy Docker feature).
    /// </summary>
    public class ContainerLink
    {
        /// <summary>Name of the container to link to.</summary>
        public string ContainerName { get; set; }
        /// <summary>Alias for the linked container (defaults to container name if not specified).</summary>
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
        
        /// <summary>Links this container to another container (legacy Docker feature).</summary>
        /// <param name="containerName">Name of the container to link to</param>
        /// <param name="alias">Optional alias for the linked container</param>
        /// <remarks>
        /// Container linking is a legacy Docker feature. Consider using user-defined networks instead.
        /// Links allow containers to discover each other and securely transfer information about one container to another.
        /// </remarks>
        IContainerBuilder WithLink(string containerName, string alias = null);
        
        /// <summary>Links this container to multiple other containers (legacy Docker feature).</summary>
        /// <param name="containerNames">Names of the containers to link to</param>
        IContainerBuilder WithLinks(params string[] containerNames);
        
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
        /// <summary>Add a compose file to use.</summary>
        IComposeBuilder WithComposeFile(string path);
        
        /// <summary>Add multiple compose files (for overrides/extensions).</summary>
        IComposeBuilder WithComposeFiles(params string[] paths);
        
        /// <summary>Set the project name.</summary>
        IComposeBuilder WithProjectName(string name);
        
        /// <summary>Set an environment variable for compose interpolation.</summary>
        IComposeBuilder WithEnvironment(string key, string value);
        
        /// <summary>Set multiple environment variables from a dictionary.</summary>
        IComposeBuilder WithEnvironment(IDictionary<string, string> environment);
        
        /// <summary>Load environment variables from an env file.</summary>
        IComposeBuilder WithEnvFile(string path);
        
        /// <summary>Build images before starting containers.</summary>
        IComposeBuilder WithBuild(bool build = true);
        
        /// <summary>Recreate containers even if configuration hasn't changed.</summary>
        IComposeBuilder WithForceRecreate(bool forceRecreate = true);
        
        /// <summary>Remove containers for services not defined in the compose file.</summary>
        IComposeBuilder WithRemoveOrphans(bool removeOrphans = true);
        
        /// <summary>Only operate on specific services.</summary>
        IComposeBuilder ForServices(params string[] services);
        
        /// <summary>Remove volumes on down.</summary>
        IComposeBuilder WithRemoveVolumes(bool removeVolumes = true);
        
        /// <summary>Remove images on down.</summary>
        IComposeBuilder WithRemoveImages(bool removeImages = true);
        
        /// <summary>Set a timeout for container shutdown (seconds).</summary>
        IComposeBuilder WithTimeout(int seconds);
        
        /// <summary>Scale a service to the specified number of replicas.</summary>
        IComposeBuilder WithScale(string service, int replicas);
        
        /// <summary>Don't start linked services.</summary>
        IComposeBuilder WithNoDeps(bool noDeps = true);
        
        /// <summary>Don't start the project (useful for testing config only).</summary>
        IComposeBuilder WithNoStart(bool noStart = true);
        
        /// <summary>Always pull images before running.</summary>
        IComposeBuilder WithPull(bool always = true);
        
        /// <summary>Wait for services to be healthy before considering them started.</summary>
        IComposeBuilder WithWait(bool wait = true);
        
        /// <summary>Set the wait timeout (seconds).</summary>
        IComposeBuilder WithWaitTimeout(int seconds);
        
        /// <summary>Use a custom profiles set.</summary>
        IComposeBuilder WithProfiles(params string[] profiles);
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
        private readonly List<ContainerLink> _links = new List<ContainerLink>();
        
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

        public IContainerBuilder WithLink(string containerName, string alias = null)
        {
            _links.Add(new ContainerLink 
            { 
                ContainerName = containerName, 
                Alias = alias ?? containerName 
            });
            return this;
        }

        public IContainerBuilder WithLinks(params string[] containerNames)
        {
            foreach (var name in containerNames)
            {
                _links.Add(new ContainerLink { ContainerName = name, Alias = name });
            }
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
                AutoRemove = _autoRemove,
                Links = _links.Count > 0 
                    ? _links.Select(l => l.Alias != l.ContainerName 
                        ? $"{l.ContainerName}:{l.Alias}" 
                        : l.ContainerName).ToList() 
                    : null
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

            // If container has links, defer starting until after all containers are created
            // This allows linked containers to be running first
            bool hasLinks = _links.Count > 0;

            if (!hasLinks)
            {
                // Start the container immediately if no links
                await service.StartAsync(cancellationToken);

                // Wait for container to actually be running
                // This is critical for containers that other containers will link to
                await WaitForContainerRunningAsync(driver, context, response.Data.Id, cancellationToken);

                // Execute "on running" lifecycle hooks
                await ExecuteLifecycleHooksAsync(service, ServiceRunningState.Running, cancellationToken);

                // Execute wait conditions
                await ExecuteWaitConditionsAsync(service, cancellationToken);
            }
            // Note: Containers with links will be started by Builder.StartContainersWithLinksAsync

            return service;
        }

        /// <summary>
        /// Waits for a container to be in running state.
        /// </summary>
        private async Task WaitForContainerRunningAsync(
            Drivers.IContainerDriver driver,
            DriverContext context,
            string containerId,
            CancellationToken cancellationToken)
        {
            const int maxAttempts = 30; // 30 attempts * 100ms = 3 seconds max
            const int delayMs = 100;

            for (int i = 0; i < maxAttempts; i++)
            {
                var inspectResult = await driver.InspectAsync(context, containerId, cancellationToken);
                if (inspectResult.Success && inspectResult.Data?.State?.Running == true)
                {
                    return;
                }

                await Task.Delay(delayMs, cancellationToken);
            }

            // If we get here, container didn't start in time, but let Docker handle the error
            // when the next container with links tries to start
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

            // Check if network already exists
            var listResult = await driver.ListAsync(context, null, cancellationToken);
            if (listResult.Success)
            {
                var existingNetwork = listResult.Data?.FirstOrDefault(n =>
                    string.Equals(n.Name, _name, StringComparison.OrdinalIgnoreCase));

                if (existingNetwork != null)
                {
                    // Network exists - if RemoveOnDispose is set, remove it first to recreate
                    if (_removeOnDispose)
                    {
                        await driver.RemoveAsync(context, existingNetwork.Id, cancellationToken);
                    }
                    else
                    {
                        // Reuse existing network
                        return new Services.Impl.NetworkService(
                            _kernel, _driverId, existingNetwork.Id, _name, _removeOnDispose);
                    }
                }
            }

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
        private readonly List<string> _envFiles = new List<string>();
        private readonly List<string> _profiles = new List<string>();
        private string _projectName;
        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _scale = new Dictionary<string, int>();
        private bool _build;
        private bool _forceRecreate;
        private bool _removeOrphans;
        private bool _removeVolumes;
        private bool _removeImages;
        private bool _noDeps;
        private bool _noStart;
        private bool _pull;
        private bool _wait;
        private int? _timeout;
        private int? _waitTimeout;
        private readonly List<string> _services = new List<string>();

        public ComposeBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        public IComposeBuilder WithComposeFile(string path) 
        { 
            _composeFiles.Add(path); 
            return this; 
        }
        
        public IComposeBuilder WithComposeFiles(params string[] paths) 
        { 
            _composeFiles.AddRange(paths); 
            return this; 
        }
        
        public IComposeBuilder WithProjectName(string name) 
        { 
            _projectName = name; 
            return this; 
        }
        
        public IComposeBuilder WithEnvironment(string key, string value) 
        { 
            _environment[key] = value; 
            return this; 
        }
        
        public IComposeBuilder WithEnvironment(IDictionary<string, string> environment) 
        { 
            foreach (var kvp in environment)
                _environment[kvp.Key] = kvp.Value;
            return this; 
        }
        
        public IComposeBuilder WithEnvFile(string path) 
        { 
            _envFiles.Add(path);
            // Also load the env file into the environment dictionary
            if (System.IO.File.Exists(path))
            {
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;
                    
                    var eqIndex = trimmed.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        var key = trimmed.Substring(0, eqIndex);
                        var value = trimmed.Substring(eqIndex + 1);
                        _environment[key] = value;
                    }
                }
            }
            return this; 
        }
        
        public IComposeBuilder WithBuild(bool build = true) 
        { 
            _build = build; 
            return this; 
        }
        
        public IComposeBuilder WithForceRecreate(bool forceRecreate = true) 
        { 
            _forceRecreate = forceRecreate; 
            return this; 
        }
        
        public IComposeBuilder WithRemoveOrphans(bool removeOrphans = true) 
        { 
            _removeOrphans = removeOrphans; 
            return this; 
        }
        
        public IComposeBuilder WithRemoveVolumes(bool removeVolumes = true) 
        { 
            _removeVolumes = removeVolumes; 
            return this; 
        }
        
        public IComposeBuilder WithRemoveImages(bool removeImages = true) 
        { 
            _removeImages = removeImages; 
            return this; 
        }
        
        public IComposeBuilder ForServices(params string[] services) 
        { 
            _services.AddRange(services); 
            return this; 
        }
        
        public IComposeBuilder WithTimeout(int seconds) 
        { 
            _timeout = seconds; 
            return this; 
        }
        
        public IComposeBuilder WithScale(string service, int replicas) 
        { 
            _scale[service] = replicas; 
            return this; 
        }
        
        public IComposeBuilder WithNoDeps(bool noDeps = true) 
        { 
            _noDeps = noDeps; 
            return this; 
        }
        
        public IComposeBuilder WithNoStart(bool noStart = true) 
        { 
            _noStart = noStart; 
            return this; 
        }
        
        public IComposeBuilder WithPull(bool always = true) 
        { 
            _pull = always; 
            return this; 
        }
        
        public IComposeBuilder WithWait(bool wait = true) 
        { 
            _wait = wait; 
            return this; 
        }
        
        public IComposeBuilder WithWaitTimeout(int seconds) 
        { 
            _waitTimeout = seconds;
            _wait = true; // Automatically enable wait when setting timeout
            return this; 
        }
        
        public IComposeBuilder WithProfiles(params string[] profiles) 
        { 
            _profiles.AddRange(profiles); 
            return this; 
        }

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
                Detached = true,
                NoDeps = _noDeps,
                NoStart = _noStart,
                Wait = _wait,
                WaitTimeout = _waitTimeout,
                Timeout = _timeout
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
