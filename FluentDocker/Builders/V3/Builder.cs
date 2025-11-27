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
        IContainerBuilder WithPort(string containerPort, string hostPort);
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
        private string _workingDir;
        private string _user;
        private string _restartPolicy;
        private string _hostname;
        private string _networkMode;
        private long? _memoryLimit;
        private long? _cpuShares;
        private bool _privileged;
        private bool _autoRemove;

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

        public IContainerBuilder WithPort(string containerPort, string hostPort)
        {
            _ports[containerPort] = hostPort;
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

            // Return async service implementation
            return new Services.V3.Impl.ContainerServiceAsync(
                _kernel,
                _driverId,
                response.Data.Id,
                _image,
                _name);
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
            return new Services.V3.Impl.NetworkServiceAsync(
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
            return new Services.V3.Impl.VolumeServiceAsync(
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
            return new Services.V3.Impl.ComposeServiceAsync(
                _kernel,
                _driverId,
                _composeFiles,
                response.Data.ProjectName ?? _projectName);
        }
    }
}
