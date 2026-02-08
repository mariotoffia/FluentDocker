using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// Fluent builder for creating and configuring a FluentDockerKernel.
    /// </summary>
    public class KernelBuilder : IKernelBuilder
    {
        private readonly List<DriverConfiguration> _driverConfigurations = new List<DriverConfiguration>();

        /// <summary>
        /// Registers a driver with lambda configuration.
        /// </summary>
        public IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure)
        {
            if (string.IsNullOrWhiteSpace(driverId))
                throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));

            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var driverBuilder = new DriverBuilder(driverId);
            configure(driverBuilder);

            _driverConfigurations.Add(driverBuilder.Build());

            return this;
        }

        /// <summary>
        /// Builds the kernel synchronously (TERMINAL operation).
        /// </summary>
        /// <remarks>
        /// For async contexts (ASP.NET, UI applications), prefer <see cref="BuildAsync"/> to avoid deadlocks.
        /// This method is safe to use in console apps, test fixtures, and scripts.
        /// </remarks>
        public FluentDockerKernel Build()
        {
            // Use Task.Run to avoid deadlocks in sync-over-async scenarios
            return Task.Run(() => BuildAsync()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds the kernel asynchronously (TERMINAL operation).
        /// </summary>
        public async Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default)
        {
            var kernel = new FluentDockerKernel();

            // Register all configured drivers
            foreach (var config in _driverConfigurations)
            {
                if (config.DriverPack != null)
                {
                    // Register as driver pack
                    await kernel.RegisterDriverPackAsync(
                        config.DriverId,
                        config.DriverPack,
                        config.Context,
                        cancellationToken);
                }
                else if (config.Driver != null)
                {
                    // Register as regular driver
                    await kernel.RegisterDriverAsync(
                        config.DriverId,
                        config.Driver,
                        config.Context,
                        cancellationToken);
                }

                if (config.IsDefault)
                {
                    kernel.SetDefaultDriver(config.DriverId);
                }
            }

            return kernel;
        }

        internal class DriverConfiguration
        {
            public string DriverId { get; set; }
            public IDriver Driver { get; set; }
            public IDriverPack DriverPack { get; set; }
            public DriverContext Context { get; set; }
            public bool IsDefault { get; set; }
        }
    }

    /// <summary>
    /// Builder for configuring a specific driver.
    /// </summary>
    internal class DriverBuilder(string driverId) : IDriverBuilder
    {
        private readonly string _driverId = driverId;
        private IDriver _driver;
        private IDriverPack _driverPack;
        private string _host;
        private string _certificatePath;
        private bool _isDefault;
        private AutoStartMachineConfig _autoStartMachine;

        public IDriverBuilder UseDockerCli()
        {
            // Use the new modular driver pack architecture
            _driverPack = new DockerCliDriverPack();
            _driver = null; // Clear any previously set driver
            return this;
        }

        public IDriverBuilder UseDockerApi()
        {
            throw new NotImplementedException("Docker API driver not yet implemented");
        }

        public IDriverBuilder UsePodmanCli()
        {
            _driverPack = new PodmanCliDriverPack();
            _driver = null;
            return this;
        }

        public IDriverBuilder UseCustomDriver(IDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _driverPack = null; // Clear any previously set driver pack
            return this;
        }

        public IDriverBuilder UseCustomDriverPack(IDriverPack driverPack)
        {
            _driverPack = driverPack ?? throw new ArgumentNullException(nameof(driverPack));
            _driver = null; // Clear any previously set driver
            return this;
        }

        public IDriverBuilder AtHost(string host)
        {
            _host = host;
            return this;
        }

        public IDriverBuilder WithCertificates(string certificatePath)
        {
            _certificatePath = certificatePath;
            return this;
        }

        public IDriverBuilder AsDefault()
        {
            _isDefault = true;
            return this;
        }

        public IDriverBuilder WithAutoStartMachine(Action<AutoStartMachineConfig> configure = null)
        {
            _autoStartMachine = new AutoStartMachineConfig();
            configure?.Invoke(_autoStartMachine);
            return this;
        }

        internal KernelBuilder.DriverConfiguration Build()
        {
            if (_driver == null && _driverPack == null)
            {
                throw new InvalidOperationException($"No driver or driver pack specified for driver ID '{_driverId}'");
            }

            var context = new DriverContext(_driverId)
            {
                Host = _host,
                CertificatePath = _certificatePath,
                AutoStartMachine = _autoStartMachine
            };

            return new KernelBuilder.DriverConfiguration
            {
                DriverId = _driverId,
                Driver = _driver,
                DriverPack = _driverPack,
                Context = context,
                IsDefault = _isDefault
            };
        }
    }
}
