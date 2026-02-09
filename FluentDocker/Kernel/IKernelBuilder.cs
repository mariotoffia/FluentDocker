using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// Fluent builder for creating and configuring a FluentDockerKernel.
    /// </summary>
    public interface IKernelBuilder
    {
        /// <summary>
        /// Registers a Docker CLI driver with type-safe configuration.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="configure">Docker CLI-specific configuration action</param>
        IKernelBuilder WithDockerCli(string driverId, Action<IDockerCliDriverBuilder> configure);

        /// <summary>
        /// Registers a Docker API driver with type-safe configuration.
        /// Communicates directly with the Docker Engine REST API.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="configure">Docker API-specific configuration action</param>
        IKernelBuilder WithDockerApi(string driverId, Action<IDockerApiDriverBuilder> configure);

        /// <summary>
        /// Registers a Podman CLI driver with type-safe configuration.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="configure">Podman CLI-specific configuration action</param>
        IKernelBuilder WithPodmanCli(string driverId, Action<IPodmanCliDriverBuilder> configure);

        /// <summary>
        /// Registers a custom driver with generic configuration.
        /// Use <see cref="WithDockerCli"/>, <see cref="WithDockerApi"/>, or
        /// <see cref="WithPodmanCli"/> for type-safe built-in driver configuration.
        /// </summary>
        /// <param name="driverId">Unique driver identifier</param>
        /// <param name="configure">Driver configuration action</param>
        IKernelBuilder WithDriver(string driverId, Action<IDriverBuilder> configure);

        /// <summary>
        /// Builds the kernel synchronously (TERMINAL operation).
        /// </summary>
        /// <remarks>
        /// For async contexts (ASP.NET, UI applications), prefer <see cref="BuildAsync"/> to avoid deadlocks.
        /// This method is safe to use in console apps, test fixtures, and scripts.
        /// </remarks>
        FluentDockerKernel Build();

        /// <summary>
        /// Builds the kernel asynchronously (TERMINAL operation).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<FluentDockerKernel> BuildAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Builder for configuring a custom driver.
    /// For built-in drivers, prefer the type-safe methods on <see cref="IKernelBuilder"/>:
    /// <see cref="IKernelBuilder.WithDockerCli"/>, <see cref="IKernelBuilder.WithDockerApi"/>,
    /// <see cref="IKernelBuilder.WithPodmanCli"/>.
    /// </summary>
    public interface IDriverBuilder
    {
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
    }
}
