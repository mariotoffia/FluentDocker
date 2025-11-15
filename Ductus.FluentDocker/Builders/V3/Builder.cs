using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Kernel;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders.V3
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
    }

    /// <summary>
    /// Network builder for lambda configuration.
    /// </summary>
    public interface INetworkBuilder
    {
        INetworkBuilder WithName(string name);
        INetworkBuilder UseDriver(string driver);
    }

    /// <summary>
    /// Volume builder for lambda configuration.
    /// </summary>
    public interface IVolumeBuilder
    {
        IVolumeBuilder WithName(string name);
        IVolumeBuilder UseDriver(string driver);
    }

    /// <summary>
    /// Container builder implementation (stub - will be properly implemented).
    /// </summary>
    internal class ContainerBuilder : IContainerBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private string _image;
        private string _name;
        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _ports = new Dictionary<string, string>();

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

        public async Task<Services.V3.IServiceAsync> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Create container using driver
            var driver = _kernel.SysCtl<Drivers.IContainerDriver>(_driverId);
            var context = new Model.Drivers.DriverContext(_driverId);

            var config = new Drivers.ContainerCreateConfig
            {
                Image = _image,
                Name = _name,
                Environment = _environment,
                PortBindings = _ports
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
    /// Network builder implementation (stub).
    /// </summary>
    internal class NetworkBuilder : INetworkBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private string _name;
        private string _driver = "bridge";

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

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            throw new NotImplementedException("Network creation will be implemented in Phase 5");
        }
    }

    /// <summary>
    /// Volume builder implementation (stub).
    /// </summary>
    internal class VolumeBuilder : IVolumeBuilder
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private string _name;
        private string _driver = "local";

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

        public async Task<IService> ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            throw new NotImplementedException("Volume creation will be implemented in Phase 5");
        }
    }
}
