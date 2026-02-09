using System;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Kernel
{
    /// <summary>
    /// Type-safe builder for configuring the Podman CLI driver.
    /// Exposes only settings applicable to the Podman CLI driver,
    /// including machine management and pod support.
    /// </summary>
    public interface IPodmanCliDriverBuilder
    {
        /// <summary>
        /// Sets the Podman daemon host URI.
        /// </summary>
        /// <param name="host">Host URI (e.g., "unix:///run/podman/podman.sock")</param>
        IPodmanCliDriverBuilder AtHost(string host);

        /// <summary>
        /// Stores the certificate path in the driver context.
        /// Note: Podman CLI does not support TLS certificate flags — this
        /// value is stored but not applied to CLI commands. Use
        /// <see cref="AtHost"/> with a remote socket URI instead.
        /// </summary>
        /// <param name="certificatePath">Path to certificate directory</param>
        IPodmanCliDriverBuilder WithCertificates(string certificatePath);

        /// <summary>
        /// Sets this driver as the default.
        /// </summary>
        IPodmanCliDriverBuilder AsDefault();

        /// <summary>
        /// Configures automatic Podman machine management during driver initialization.
        /// When enabled, the driver will ensure a machine is running before completing
        /// initialization (macOS/Windows only).
        /// </summary>
        /// <param name="configure">
        /// Optional configuration action. When null, uses defaults
        /// (start the default machine if it exists but is not running).
        /// </param>
        IPodmanCliDriverBuilder WithAutoStartMachine(Action<AutoStartMachineConfig> configure = null);

        /// <summary>
        /// Configures sudo mechanism for Podman CLI commands (Linux).
        /// </summary>
        /// <param name="mechanism">Sudo mechanism to use</param>
        /// <param name="password">Password when using <see cref="SudoMechanism.Password"/></param>
        IPodmanCliDriverBuilder WithSudo(SudoMechanism mechanism, string password = null);
    }
}
