using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// Fluent builder for creating and configuring a FluentDockerKernel.
    /// </summary>
    public class KernelBuilder : IKernelBuilder
    {
        private readonly List<DriverConfiguration> _driverConfigurations = new();

        /// <inheritdoc />
        public IKernelBuilder WithDockerCli(string driverId, Action<IDockerCliDriverBuilder> configure)
        {
            ValidateDriverArgs(driverId, configure);
            var builder = new DockerCliDriverBuilder(driverId);
            configure(builder);
            _driverConfigurations.Add(builder.Build());
            return this;
        }

        /// <inheritdoc />
        public IKernelBuilder WithDockerApi(string driverId, Action<IDockerApiDriverBuilder> configure)
        {
            ValidateDriverArgs(driverId, configure);
            var builder = new DockerApiDriverBuilder(driverId);
            configure(builder);
            _driverConfigurations.Add(builder.Build());
            return this;
        }

        /// <inheritdoc />
        public IKernelBuilder WithPodmanCli(string driverId, Action<IPodmanCliDriverBuilder> configure)
        {
            ValidateDriverArgs(driverId, configure);
            var builder = new PodmanCliDriverBuilder(driverId);
            configure(builder);
            _driverConfigurations.Add(builder.Build());
            return this;
        }

        /// <inheritdoc />
        public IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure)
        {
            ValidateDriverArgs(driverId, configure);
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
            return Task.Run(() => BuildAsync()).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default)
        {
            var kernel = new FluentDockerKernel();

            foreach (var config in _driverConfigurations)
            {
                if (config.DriverPack != null)
                {
                    await kernel.RegisterDriverPackAsync(
                        config.DriverId, config.DriverPack, config.Context, cancellationToken);
                }
                else if (config.Driver != null)
                {
                    await kernel.RegisterDriverAsync(
                        config.DriverId, config.Driver, config.Context, cancellationToken);
                }

                if (config.IsDefault)
                    kernel.SetDefaultDriver(config.DriverId);
            }

            return kernel;
        }

        private static void ValidateDriverArgs<T>(string driverId, Action<T> configure)
        {
            if (string.IsNullOrWhiteSpace(driverId))
                throw new ArgumentException("Driver ID cannot be null or empty", nameof(driverId));
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));
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
    /// Builder for configuring a custom driver (via <see cref="IKernelBuilder.WithDriver"/>).
    /// </summary>
    internal class DriverBuilder(string driverId) : IDriverBuilder
    {
        private readonly string _driverId = driverId;
        private IDriver _driver;
        private IDriverPack _driverPack;
        private string _host;
        private string _certificatePath;
        private bool _isDefault;

        public IDriverBuilder UseCustomDriver(IDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _driverPack = null;
            return this;
        }

        public IDriverBuilder UseCustomDriverPack(IDriverPack driverPack)
        {
            _driverPack = driverPack ?? throw new ArgumentNullException(nameof(driverPack));
            _driver = null;
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
            if (_driver == null && _driverPack == null)
            {
                throw new InvalidOperationException(
                    $"No driver or driver pack specified for driver ID '{_driverId}'");
            }

            var context = new DriverContext(_driverId)
            {
                Host = _host,
                CertificatePath = _certificatePath,
            };

            return new KernelBuilder.DriverConfiguration
            {
                DriverId = _driverId,
                Driver = _driver,
                DriverPack = _driverPack,
                Context = context,
                IsDefault = _isDefault,
            };
        }
    }
}
