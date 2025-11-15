using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Kernel
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
        /// Builds the kernel asynchronously (TERMINAL operation).
        /// </summary>
        public async Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default)
        {
            var kernel = new FluentDockerKernel();

            // Register all configured drivers
            foreach (var config in _driverConfigurations)
            {
                await kernel.RegisterDriverAsync(
                    config.DriverId,
                    config.Driver,
                    config.Context,
                    cancellationToken);

                if (config.IsDefault)
                {
                    kernel.SetDefaultDriver(config.DriverId);
                }
            }

            return kernel;
        }

        private class DriverConfiguration
        {
            public string DriverId { get; set; }
            public IDriver Driver { get; set; }
            public DriverContext Context { get; set; }
            public bool IsDefault { get; set; }
        }
    }

    /// <summary>
    /// Builder for configuring a specific driver.
    /// </summary>
    internal class DriverBuilder : IDriverBuilder
    {
        private readonly string _driverId;
        private IDriver _driver;
        private string _host;
        private string _certificatePath;
        private bool _isDefault;

        public DriverBuilder(string driverId)
        {
            _driverId = driverId;
        }

        public IDriverBuilder UseDockerCli()
        {
            _driver = new Drivers.Docker.Cli.DockerCliDriver();
            return this;
        }

        public IDriverBuilder UseDockerApi()
        {
            throw new NotImplementedException("Docker API driver not yet implemented");
        }

        public IDriverBuilder UsePodmanCli()
        {
            throw new NotImplementedException("Podman CLI driver not yet implemented");
        }

        public IDriverBuilder UseCustomDriver(IDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
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

        internal KernelBuilder.DriverConfiguration Build()
        {
            if (_driver == null)
            {
                throw new InvalidOperationException($"No driver specified for driver ID '{_driverId}'");
            }

            var context = new DriverContext(_driverId)
            {
                Host = _host,
                CertificatePath = _certificatePath
            };

            return new KernelBuilder.DriverConfiguration
            {
                DriverId = _driverId,
                Driver = _driver,
                Context = context,
                IsDefault = _isDefault
            };
        }
    }
}
