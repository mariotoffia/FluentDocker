using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// Fluent builder for creating and configuring a FluentDockerKernel.
    /// </summary>
    public interface IKernelBuilder
    {
        /// <summary>
        /// Registers a driver with lambda configuration.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="configure">Driver configuration action</param>
        /// <returns>This builder for fluent chaining</returns>
        IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure);

        /// <summary>
        /// Builds the kernel synchronously (TERMINAL operation).
        /// </summary>
        /// <remarks>
        /// For async contexts (ASP.NET, UI applications), prefer <see cref="BuildAsync"/> to avoid deadlocks.
        /// This method is safe to use in console apps, test fixtures, and scripts.
        /// </remarks>
        /// <returns>Configured FluentDockerKernel instance</returns>
        FluentDockerKernel Build();

        /// <summary>
        /// Builds the kernel asynchronously (TERMINAL operation).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Configured FluentDockerKernel instance</returns>
        Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Builder for configuring a specific driver.
    /// </summary>
    public interface IDriverBuilder
    {
        /// <summary>
        /// Uses Docker CLI driver pack (modular architecture).
        /// This is the recommended approach for new code.
        /// </summary>
        IDriverBuilder UseDockerCli();

        /// <summary>
        /// Uses Docker API driver.
        /// </summary>
        IDriverBuilder UseDockerApi();

        /// <summary>
        /// Uses Podman CLI driver.
        /// </summary>
        IDriverBuilder UsePodmanCli();

        /// <summary>
        /// Uses a custom driver instance.
        /// </summary>
        IDriverBuilder UseCustomDriver(IDriver driver);

        /// <summary>
        /// Uses a custom driver pack instance.
        /// </summary>
        IDriverBuilder UseCustomDriverPack(IDriverPack driverPack);

        /// <summary>
        /// Sets the host for this driver.
        /// </summary>
        /// <param name="host">Host URI (e.g., "unix:///var/run/docker.sock", "tcp://localhost:2376")</param>
        IDriverBuilder AtHost(string host);

        /// <summary>
        /// Sets the certificate path for TLS connections.
        /// </summary>
        /// <param name="certificatePath">Path to certificate directory</param>
        IDriverBuilder WithCertificates(string certificatePath);

        /// <summary>
        /// Sets this driver as the default.
        /// </summary>
        IDriverBuilder AsDefault();

        /// <summary>
        /// Configures automatic Podman machine management during driver initialization.
        /// When enabled, the Podman driver will ensure a machine is running before
        /// completing initialization. Ignored by non-Podman drivers.
        /// </summary>
        /// <param name="configure">
        /// Optional configuration action. When null, uses defaults
        /// (start the default machine if it exists but is not running).
        /// </param>
        IDriverBuilder WithAutoStartMachine(Action<AutoStartMachineConfig> configure = null);
    }
}
