using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
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

    /// <summary>
    /// Container builder for lambda configuration.
    /// </summary>
    public interface IContainerBuilder
    {
        IContainerBuilder UseImage(string image);
        IContainerBuilder WithName(string name);
        IContainerBuilder WithEnvironment(string key, string value);
        /// <summary>
        /// Sets an environment variable using "KEY=VALUE" format.
        /// </summary>
        IContainerBuilder WithEnvironment(string keyValue);
        IContainerBuilder WithPort(string containerPort, string hostPort);
        /// <summary>
        /// Exposes a container port, letting Docker assign a random host port.
        /// </summary>
        /// <param name="containerPort">The container port to expose (e.g., "5432/tcp" or just "5432").</param>
        IContainerBuilder ExposePort(string containerPort);
        /// <summary>
        /// Exposes a container port with explicit host port mapping.
        /// </summary>
        /// <param name="hostPort">The host port.</param>
        /// <param name="containerPort">The container port.</param>
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
        IContainerBuilder WithMemoryLimit(long bytes);
        IContainerBuilder WithCpuShares(long shares);
        IContainerBuilder WithPrivileged(bool privileged = true);
        IContainerBuilder WithAutoRemove(bool autoRemove = true);
        
        /// <summary>
        /// Configures the container to wait for a port to be available after starting.
        /// </summary>
        /// <param name="portAndProto">Port and protocol, e.g., "5432/tcp".</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 30000).</param>
        IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000);
        
        /// <summary>
        /// Configures the container to wait for a process to be running after starting.
        /// </summary>
        /// <param name="processName">Name of the process to wait for.</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 30000).</param>
        IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000);
        
        /// <summary>
        /// Configures the container to wait for an HTTP endpoint to respond after starting.
        /// </summary>
        /// <param name="portAndProto">Port and protocol, e.g., "8080/tcp".</param>
        /// <param name="path">URL path to check, e.g., "/health".</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 30000).</param>
        IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000);
        
        /// <summary>
        /// Configures the container to wait for a specific message in logs after starting.
        /// </summary>
        /// <param name="message">Message to wait for.</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 30000).</param>
        IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000);
        
        /// <summary>
        /// Keeps the container after dispose (don't delete).
        /// </summary>
        IContainerBuilder KeepContainer();
        
        /// <summary>
        /// Keeps the container running after dispose (don't stop).
        /// </summary>
        IContainerBuilder KeepRunning();
    }

    /// <summary>
    /// Network builder for lambda configuration.
    /// </summary>
    public interface INetworkBuilder
    {
        INetworkBuilder WithName(string name);
        INetworkBuilder UseDriver(string driver);
        INetworkBuilder WithSubnet(string subnet);
        INetworkBuilder WithGateway(string gateway);
        INetworkBuilder WithIPv6(bool enableIPv6 = true);
        INetworkBuilder WithLabel(string key, string value);
        INetworkBuilder WithOption(string key, string value);
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
    }

    /// <summary>
    /// Defines a wait condition for the container.
    /// </summary>
    internal enum WaitConditionType
    {
        Port,
        Process,
        Http,
        LogMessage
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
    }

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
        private readonly List<WaitCondition> _waitConditions = new List<WaitCondition>();
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

        public ContainerBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

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
            // Normalize to "port/protocol" format
            var normalized = containerPort.Contains("/") ? containerPort : $"{containerPort}/tcp";
            _ports[normalized] = ""; // Empty string = random host port
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
                TimeoutMs = timeoutMs
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

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Create container using driver
            var driver = _kernel.SysCtl<Drivers.IContainerDriver>(_driverId);
            var context = new Model.Drivers.DriverContext(_driverId);

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
                throw new Common.DriverException(
                    $"Failed to create container: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            // Create service
            var service = new Services.Impl.ContainerService(
                _kernel,
                _driverId,
                response.Data.Id,
                _image,
                _name,
                !_keepRunning,  // stopOnDispose
                !_keepContainer); // deleteOnDispose

            // Start the container
            await service.StartAsync(cancellationToken);

            // Execute wait conditions
            foreach (var condition in _waitConditions)
            {
                bool success;
                switch (condition.Type)
                {
                    case WaitConditionType.Port:
                        success = await Services.Extensions.ServiceExtensions.WaitForPortAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new Common.FluentDockerException(
                                $"Timeout waiting for port {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.Process:
                        success = await Services.Extensions.ServiceExtensions.WaitForProcessAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new Common.FluentDockerException(
                                $"Timeout waiting for process {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.Http:
                        success = await Services.Extensions.ServiceExtensions.WaitForHttpAsync(
                            service, condition.Target, condition.Path, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new Common.FluentDockerException(
                                $"Timeout waiting for HTTP {condition.Path} on port {condition.Target} on container {service.Id}");
                        break;

                    case WaitConditionType.LogMessage:
                        success = await Services.Extensions.ServiceExtensions.WaitForLogMessageAsync(
                            service, condition.Target, condition.TimeoutMs, cancellationToken);
                        if (!success)
                            throw new Common.FluentDockerException(
                                $"Timeout waiting for log message '{condition.Target}' on container {service.Id}");
                        break;
                }
            }

            return service;
        }
    }

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
        private bool _enableIPv6;
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _options = new Dictionary<string, string>();

        public NetworkBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        public INetworkBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

        public INetworkBuilder UseDriver(string driver)
        {
            _driver = driver;
            return this;
        }

        public INetworkBuilder WithSubnet(string subnet)
        {
            _subnet = subnet;
            return this;
        }

        public INetworkBuilder WithGateway(string gateway)
        {
            _gateway = gateway;
            return this;
        }

        public INetworkBuilder WithIPv6(bool enableIPv6 = true)
        {
            _enableIPv6 = enableIPv6;
            return this;
        }

        public INetworkBuilder WithLabel(string key, string value)
        {
            _labels[key] = value;
            return this;
        }

        public INetworkBuilder WithOption(string key, string value)
        {
            _options[key] = value;
            return this;
        }

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Create network using driver
            var driver = _kernel.SysCtl<Drivers.INetworkDriver>(_driverId);
            var context = new Model.Drivers.DriverContext(_driverId);

            var config = new Drivers.NetworkCreateConfig
            {
                Name = _name,
                Driver = _driver,
                Subnet = _subnet,
                Gateway = _gateway,
                EnableIPv6 = _enableIPv6,
                Labels = _labels,
                Options = _options
            };

            var response = await driver.CreateAsync(context, config, cancellationToken);

            if (!response.Success)
            {
                throw new Common.DriverException(
                    $"Failed to create network: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            // Return async service implementation
            return new Services.Impl.NetworkService(
                _kernel,
                _driverId,
                response.Data.Id,
                _name);
        }
    }

    /// <summary>
    /// Volume builder implementation.
    /// </summary>
    internal class VolumeBuilder : IVolumeBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private string _name;
        private string _driver = "local";
        private readonly Dictionary<string, string> _driverOpts = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();

        public VolumeBuilder(FluentDockerKernel kernel, string driverId)
        {
            _kernel = kernel;
            _driverId = driverId;
        }

        public IVolumeBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

        public IVolumeBuilder UseDriver(string driver)
        {
            _driver = driver;
            return this;
        }

        public IVolumeBuilder WithDriverOption(string key, string value)
        {
            _driverOpts[key] = value;
            return this;
        }

        public IVolumeBuilder WithLabel(string key, string value)
        {
            _labels[key] = value;
            return this;
        }

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Create volume using driver
            var driver = _kernel.SysCtl<Drivers.IVolumeDriver>(_driverId);
            var context = new Model.Drivers.DriverContext(_driverId);

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
                throw new Common.DriverException(
                    $"Failed to create volume: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            // Return async service implementation
            return new Services.Impl.VolumeService(
                _kernel,
                _driverId,
                response.Data.Name,
                _driver);
        }
    }

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

        public IComposeBuilder ForServices(params string[] services)
        {
            _services.AddRange(services);
            return this;
        }

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Start compose using driver
            var driver = _kernel.SysCtl<Drivers.IComposeDriver>(_driverId);
            var context = new Model.Drivers.DriverContext(_driverId);

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
                throw new Common.DriverException(
                    $"Failed to start compose: {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            // Return async service implementation
            return new Services.Impl.ComposeService(
                _kernel,
                _driverId,
                _composeFiles,
                response.Data.ProjectName ?? _projectName);
        }
    }
}
